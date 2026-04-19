#include "provisioning_transport.h"

#include <cstring>
#include <cctype>
#include <cstdio>

#include <esp_bt.h>
#include <esp_check.h>
#include <esp_heap_caps.h>
#include <esp_log.h>
#include <freertos/FreeRTOS.h>
#include <freertos/task.h>

#include <nimble/nimble_port.h>
#include <nimble/nimble_port_freertos.h>
#include <host/ble_hs.h>
#include <host/util/util.h>
#include <services/gap/ble_svc_gap.h>
#include <services/gatt/ble_svc_gatt.h>

static const char *TAG = "ProvisioningBle";
static provisioning_payload_t s_pending_payload = {};
static bool s_has_pending_payload = false;
static bool s_ble_initialized = false;
static bool s_ble_advertising = false;
static bool s_ble_synced = false;
static bool s_should_advertise = false;
static bool s_gatt_initialized = false;

static constexpr size_t REASSEMBLY_BUFFER_SIZE = 1024;
static constexpr TickType_t CHUNK_TIMEOUT_TICKS = pdMS_TO_TICKS(2000);
static char s_reassembly_buffer[REASSEMBLY_BUFFER_SIZE] = {};
static size_t s_reassembly_length = 0;
static TickType_t s_last_chunk_tick = 0;

static char s_status_value[96] = "idle";

static constexpr uint8_t PROVISIONING_SERVICE_UUID[16] = {
    0x41, 0x20, 0x93, 0x44, 0xA8, 0x1D, 0x4B, 0x9E,
    0x8D, 0x8E, 0x3A, 0xD9, 0xCA, 0x3A, 0xE0, 0x01};
static constexpr uint8_t PROVISIONING_CONFIG_CHAR_UUID[16] = {
    0x41, 0x20, 0x93, 0x44, 0xA8, 0x1D, 0x4B, 0x9E,
    0x8D, 0x8E, 0x3A, 0xD9, 0xCA, 0x3A, 0xE0, 0x02};
static constexpr uint8_t PROVISIONING_STATUS_CHAR_UUID[16] = {
    0x41, 0x20, 0x93, 0x44, 0xA8, 0x1D, 0x4B, 0x9E,
    0x8D, 0x8E, 0x3A, 0xD9, 0xCA, 0x3A, 0xE0, 0x03};

static int gatt_access_cb(uint16_t conn_handle, uint16_t attr_handle, struct ble_gatt_access_ctxt *ctxt, void *arg);
static void start_advertising(void);
static void set_status(const char *message);
static void init_gatt_layout(void);
static void reset_reassembly_state(void);

static void reset_reassembly_state(void)
{
    s_reassembly_length = 0;
    s_last_chunk_tick = 0;
    s_reassembly_buffer[0] = '\0';
}

static void copy_string(char *destination, size_t destination_size, const char *source)
{
    if (destination == nullptr || destination_size == 0)
    {
        return;
    }

    std::strncpy(destination, source == nullptr ? "" : source, destination_size - 1);
    destination[destination_size - 1] = '\0';
}

static const ble_uuid128_t service_uuid =
    BLE_UUID128_INIT(
        PROVISIONING_SERVICE_UUID[0], PROVISIONING_SERVICE_UUID[1], PROVISIONING_SERVICE_UUID[2], PROVISIONING_SERVICE_UUID[3],
        PROVISIONING_SERVICE_UUID[4], PROVISIONING_SERVICE_UUID[5], PROVISIONING_SERVICE_UUID[6], PROVISIONING_SERVICE_UUID[7],
        PROVISIONING_SERVICE_UUID[8], PROVISIONING_SERVICE_UUID[9], PROVISIONING_SERVICE_UUID[10], PROVISIONING_SERVICE_UUID[11],
        PROVISIONING_SERVICE_UUID[12], PROVISIONING_SERVICE_UUID[13], PROVISIONING_SERVICE_UUID[14], PROVISIONING_SERVICE_UUID[15]);

static const ble_uuid128_t config_char_uuid =
    BLE_UUID128_INIT(
        PROVISIONING_CONFIG_CHAR_UUID[0], PROVISIONING_CONFIG_CHAR_UUID[1], PROVISIONING_CONFIG_CHAR_UUID[2], PROVISIONING_CONFIG_CHAR_UUID[3],
        PROVISIONING_CONFIG_CHAR_UUID[4], PROVISIONING_CONFIG_CHAR_UUID[5], PROVISIONING_CONFIG_CHAR_UUID[6], PROVISIONING_CONFIG_CHAR_UUID[7],
        PROVISIONING_CONFIG_CHAR_UUID[8], PROVISIONING_CONFIG_CHAR_UUID[9], PROVISIONING_CONFIG_CHAR_UUID[10], PROVISIONING_CONFIG_CHAR_UUID[11],
        PROVISIONING_CONFIG_CHAR_UUID[12], PROVISIONING_CONFIG_CHAR_UUID[13], PROVISIONING_CONFIG_CHAR_UUID[14], PROVISIONING_CONFIG_CHAR_UUID[15]);

static const ble_uuid128_t status_char_uuid =
    BLE_UUID128_INIT(
        PROVISIONING_STATUS_CHAR_UUID[0], PROVISIONING_STATUS_CHAR_UUID[1], PROVISIONING_STATUS_CHAR_UUID[2], PROVISIONING_STATUS_CHAR_UUID[3],
        PROVISIONING_STATUS_CHAR_UUID[4], PROVISIONING_STATUS_CHAR_UUID[5], PROVISIONING_STATUS_CHAR_UUID[6], PROVISIONING_STATUS_CHAR_UUID[7],
        PROVISIONING_STATUS_CHAR_UUID[8], PROVISIONING_STATUS_CHAR_UUID[9], PROVISIONING_STATUS_CHAR_UUID[10], PROVISIONING_STATUS_CHAR_UUID[11],
        PROVISIONING_STATUS_CHAR_UUID[12], PROVISIONING_STATUS_CHAR_UUID[13], PROVISIONING_STATUS_CHAR_UUID[14], PROVISIONING_STATUS_CHAR_UUID[15]);

    static ble_uuid128_t advertised_uuids[] = {service_uuid};

static int gap_event_cb(struct ble_gap_event *event, void *)
{
    switch (event->type)
    {
    case BLE_GAP_EVENT_CONNECT:
        if (event->connect.status == 0)
        {
            ESP_LOGI(TAG, "BLE client connected");
            s_ble_advertising = false;
            set_status("connected");
            return 0;
        }

        ESP_LOGW(TAG, "BLE connect failed (status=%d)", event->connect.status);
        if (s_should_advertise)
        {
            start_advertising();
        }
        return 0;

    case BLE_GAP_EVENT_DISCONNECT:
        ESP_LOGI(TAG, "BLE client disconnected (reason=%d)", event->disconnect.reason);
        reset_reassembly_state();
        set_status("waiting");
        if (s_should_advertise)
        {
            start_advertising();
        }
        return 0;

    case BLE_GAP_EVENT_ADV_COMPLETE:
        s_ble_advertising = false;
        if (s_should_advertise)
        {
            start_advertising();
        }
        return 0;

    default:
        return 0;
    }
}

static ble_gatt_chr_def gatt_characteristics[3] = {};
static ble_gatt_svc_def gatt_services[2] = {};

static void init_gatt_layout(void)
{
    if (s_gatt_initialized)
    {
        return;
    }

    gatt_characteristics[0] = {};
    gatt_characteristics[0].uuid = &config_char_uuid.u;
    gatt_characteristics[0].access_cb = gatt_access_cb;
    gatt_characteristics[0].flags = BLE_GATT_CHR_F_WRITE | BLE_GATT_CHR_F_WRITE_NO_RSP;

    gatt_characteristics[1] = {};
    gatt_characteristics[1].uuid = &status_char_uuid.u;
    gatt_characteristics[1].access_cb = gatt_access_cb;
    gatt_characteristics[1].flags = BLE_GATT_CHR_F_READ;

    gatt_characteristics[2] = {};

    gatt_services[0] = {};
    gatt_services[0].type = BLE_GATT_SVC_TYPE_PRIMARY;
    gatt_services[0].uuid = &service_uuid.u;
    gatt_services[0].characteristics = gatt_characteristics;

    gatt_services[1] = {};
    s_gatt_initialized = true;
}

static void set_status(const char *message)
{
    copy_string(s_status_value, sizeof(s_status_value), message);
}

static const char *skip_spaces(const char *cursor)
{
    while (cursor != nullptr && *cursor != '\0' && std::isspace(static_cast<unsigned char>(*cursor)) != 0)
    {
        ++cursor;
    }
    return cursor;
}

static bool extract_json_string(const char *json, const char *key, char *destination, size_t destination_size)
{
    if (json == nullptr || key == nullptr || destination == nullptr || destination_size == 0)
    {
        return false;
    }

    char pattern[48] = {};
    std::snprintf(pattern, sizeof(pattern), "\"%s\"", key);
    const char *key_position = std::strstr(json, pattern);
    if (key_position == nullptr)
    {
        return false;
    }

    const char *colon = std::strchr(key_position + std::strlen(pattern), ':');
    if (colon == nullptr)
    {
        return false;
    }

    const char *value_start = skip_spaces(colon + 1);
    if (value_start == nullptr || *value_start != '\"')
    {
        return false;
    }
    ++value_start;

    const char *value_end = std::strchr(value_start, '\"');
    if (value_end == nullptr)
    {
        return false;
    }

    const size_t value_len = static_cast<size_t>(value_end - value_start);
    if (value_len >= destination_size)
    {
        return false;
    }

    std::memcpy(destination, value_start, value_len);
    destination[value_len] = '\0';
    return true;
}

static bool parse_payload_json(const char *json, provisioning_payload_t *payload)
{
    if (json == nullptr || payload == nullptr)
    {
        return false;
    }

    provisioning_payload_t parsed = {};
    if (!extract_json_string(json, "device_id", parsed.config.device_id, sizeof(parsed.config.device_id)))
    {
        return false;
    }
    if (!extract_json_string(json, "ssid", parsed.config.ssid, sizeof(parsed.config.ssid)))
    {
        return false;
    }

    // Password is optional for open Wi-Fi networks.
    if (!extract_json_string(json, "password", parsed.config.password, sizeof(parsed.config.password)))
    {
        parsed.config.password[0] = '\0';
    }

    if (!extract_json_string(json, "mqtt_broker_uri", parsed.config.mqtt_uri, sizeof(parsed.config.mqtt_uri)))
    {
        return false;
    }

    parsed.has_token = extract_json_string(json, "token", parsed.token, sizeof(parsed.token));
    *payload = parsed;
    return true;
}

static int gatt_access_cb(uint16_t, uint16_t, struct ble_gatt_access_ctxt *ctxt, void *)
{
    const ble_uuid_t *uuid = ctxt->chr != nullptr ? ctxt->chr->uuid : nullptr;
    if (uuid == nullptr)
    {
        return BLE_ATT_ERR_UNLIKELY;
    }

    if (ble_uuid_cmp(uuid, &status_char_uuid.u) == 0)
    {
        if (ctxt->op != BLE_GATT_ACCESS_OP_READ_CHR)
        {
            return BLE_ATT_ERR_UNLIKELY;
        }

        return os_mbuf_append(ctxt->om, s_status_value, std::strlen(s_status_value)) == 0
                   ? 0
                   : BLE_ATT_ERR_INSUFFICIENT_RES;
    }

    if (ble_uuid_cmp(uuid, &config_char_uuid.u) != 0 || ctxt->op != BLE_GATT_ACCESS_OP_WRITE_CHR)
    {
        return BLE_ATT_ERR_UNLIKELY;
    }

    char buffer[512] = {};
    uint16_t actual_len = 0;
    const int flatten_rc = ble_hs_mbuf_to_flat(ctxt->om, buffer, sizeof(buffer) - 1, &actual_len);
    if (flatten_rc != 0)
    {
        set_status("error:payload_flatten");
        ESP_LOGE(TAG, "Failed to flatten provisioning write payload (rc=%d)", flatten_rc);
        return BLE_ATT_ERR_INVALID_ATTR_VALUE_LEN;
    }
    buffer[actual_len] = '\0';

    const TickType_t now = xTaskGetTickCount();
    if (s_reassembly_length > 0 && (now - s_last_chunk_tick) > CHUNK_TIMEOUT_TICKS)
    {
        ESP_LOGW(TAG, "Provisioning payload chunk timeout after %lu ms; discarding %u bytes",
                 (unsigned long)(CHUNK_TIMEOUT_TICKS * portTICK_PERIOD_MS),
                 (unsigned)s_reassembly_length);
        set_status("error:chunk_timeout");
        reset_reassembly_state();
    }

    if (actual_len == 0)
    {
        return 0;
    }

    if (s_reassembly_length + actual_len >= REASSEMBLY_BUFFER_SIZE)
    {
        ESP_LOGW(TAG, "Provisioning payload too large; chunk=%u accumulated=%u max=%u",
                 (unsigned)actual_len,
                 (unsigned)s_reassembly_length,
                 (unsigned)(REASSEMBLY_BUFFER_SIZE - 1));
        set_status("error:payload_too_large");
        reset_reassembly_state();
        return BLE_ATT_ERR_INVALID_ATTR_VALUE_LEN;
    }

    std::memcpy(s_reassembly_buffer + s_reassembly_length, buffer, actual_len);
    s_reassembly_length += actual_len;
    s_reassembly_buffer[s_reassembly_length] = '\0';
    s_last_chunk_tick = now;

    const char *terminator = std::strchr(s_reassembly_buffer, '}');
    if (terminator == nullptr)
    {
        ESP_LOGD(TAG, "Provisioning chunk received (%u bytes), waiting for terminator (accumulated=%u)",
                 (unsigned)actual_len,
                 (unsigned)s_reassembly_length);
        set_status("waiting");
        return 0;
    }

    const size_t message_length = static_cast<size_t>(terminator - s_reassembly_buffer) + 1;
    char assembled_message[REASSEMBLY_BUFFER_SIZE] = {};
    std::memcpy(assembled_message, s_reassembly_buffer, message_length);
    assembled_message[message_length] = '\0';

    if (s_reassembly_length > message_length)
    {
        ESP_LOGW(TAG, "Discarding %u trailing bytes after JSON terminator",
                 (unsigned)(s_reassembly_length - message_length));
    }

    reset_reassembly_state();

    provisioning_payload_t payload = {};
    if (!parse_payload_json(assembled_message, &payload))
    {
        set_status("error:invalid_json");
        ESP_LOGW(TAG, "Invalid provisioning JSON payload: %s", assembled_message);
        return BLE_ATT_ERR_UNLIKELY;
    }

    const esp_err_t submit_err = provisioning_transport_submit_ble_payload(&payload);
    if (submit_err != ESP_OK)
    {
        set_status("error:queue_failed");
        ESP_LOGE(TAG, "Failed to submit provisioning payload: %s", esp_err_to_name(submit_err));
        return BLE_ATT_ERR_UNLIKELY;
    }

    set_status("ok:queued");
    ESP_LOGI(TAG, "Provisioning JSON payload queued from BLE write");
    return 0;
}

static void start_advertising(void)
{
    if (!s_ble_synced)
    {
        return;
    }

    struct ble_hs_adv_fields fields = {};
    fields.flags = BLE_HS_ADV_F_DISC_GEN | BLE_HS_ADV_F_BREDR_UNSUP;

    static const char *DEVICE_NAME = "TccProvisioner";
    fields.name = reinterpret_cast<const uint8_t *>(DEVICE_NAME);
    fields.name_len = std::strlen(DEVICE_NAME);
    fields.name_is_complete = 1;

    int rc = ble_gap_adv_set_fields(&fields);
    if (rc != 0)
    {
        ESP_LOGE(TAG, "ble_gap_adv_set_fields failed: %d", rc);
        return;
    }

    struct ble_hs_adv_fields scan_rsp_fields = {};
    scan_rsp_fields.uuids128 = advertised_uuids;
    scan_rsp_fields.num_uuids128 = 1;
    scan_rsp_fields.uuids128_is_complete = 1;

    rc = ble_gap_adv_rsp_set_fields(&scan_rsp_fields);
    if (rc != 0)
    {
        ESP_LOGE(TAG, "ble_gap_adv_rsp_set_fields failed: %d", rc);
        return;
    }

    struct ble_gap_adv_params adv_params = {};
    adv_params.conn_mode = BLE_GAP_CONN_MODE_UND;
    adv_params.disc_mode = BLE_GAP_DISC_MODE_GEN;

    rc = ble_gap_adv_start(BLE_OWN_ADDR_PUBLIC, nullptr, BLE_HS_FOREVER, &adv_params, gap_event_cb, nullptr);
    if (rc != 0)
    {
        ESP_LOGE(TAG, "ble_gap_adv_start failed: %d", rc);
        return;
    }

    s_ble_advertising = true;
    set_status("waiting");
    ESP_LOGI(TAG, "BLE provisioning advertising started");
}

static void on_sync(void)
{
    s_ble_synced = true;

    uint8_t addr_type = 0;
    uint8_t addr_val[6] = {};
    int rc = ble_hs_id_infer_auto(0, &addr_type);
    if (rc != 0)
    {
        ESP_LOGE(TAG, "ble_hs_id_infer_auto failed: %d", rc);
        return;
    }

    rc = ble_hs_id_copy_addr(addr_type, addr_val, nullptr);
    if (rc != 0)
    {
        ESP_LOGW(TAG, "ble_hs_id_copy_addr failed: %d", rc);
    }

    ESP_LOGI(TAG, "BLE stack synced, addr_type=%d", addr_type);
    if (s_should_advertise)
    {
        start_advertising();
    }
}

static void host_task(void *)
{
    nimble_port_run();
    nimble_port_freertos_deinit();
}

static esp_err_t ensure_ble_initialized(void)
{
    if (s_ble_initialized)
    {
        return ESP_OK;
    }

    ESP_LOGI(TAG, "[BLE-INIT] free=%lu min=%lu largest=%lu",
             (unsigned long)esp_get_free_heap_size(),
             (unsigned long)esp_get_minimum_free_heap_size(),
             (unsigned long)heap_caps_get_largest_free_block(MALLOC_CAP_8BIT));

    // In ESP-IDF v6.0, nimble_port_init() owns the full controller bring-up
    // (mem_release, controller_init/enable, hci_init). Calling those manually
    // first causes a double-init failure inside nimble_port_init.
    (void)esp_bt_controller_mem_release(ESP_BT_MODE_CLASSIC_BT);

    const esp_err_t port_err = nimble_port_init();
    if (port_err != ESP_OK)
    {
        ESP_LOGE(TAG, "nimble_port_init failed: %s", esp_err_to_name(port_err));
        return port_err;
    }
    ble_hs_cfg.sync_cb = on_sync;

    init_gatt_layout();

    ble_svc_gap_init();
    ble_svc_gatt_init();
    ble_svc_gap_device_name_set("TccProvisioner");

    int rc = ble_gatts_count_cfg(gatt_services);
    if (rc == 0)
    {
        rc = ble_gatts_add_svcs(gatt_services);
    }
    if (rc != 0)
    {
        ESP_LOGE(TAG, "Failed to register GATT services: %d", rc);
        return ESP_FAIL;
    }

    nimble_port_freertos_init(host_task);
    s_ble_initialized = true;
    return ESP_OK;
}

esp_err_t provisioning_transport_start_ble(void)
{
    ESP_LOGI(TAG, "Starting BLE provisioning transport");
    ESP_RETURN_ON_ERROR(ensure_ble_initialized(), TAG, "BLE init failed");

    reset_reassembly_state();

    s_should_advertise = true;
    if (s_ble_synced && !s_ble_advertising)
    {
        start_advertising();
    }

    set_status("waiting");
    return ESP_OK;
}

esp_err_t provisioning_transport_stop_ble(void)
{
    ESP_LOGI(TAG, "Stopping BLE provisioning transport");
    reset_reassembly_state();
    s_should_advertise = false;
    if (s_ble_advertising)
    {
        (void)ble_gap_adv_stop();
        s_ble_advertising = false;
    }

    s_has_pending_payload = false;
    s_pending_payload = {};
    set_status("stopped");
    return ESP_OK;
}

bool provisioning_transport_try_get_payload(provisioning_payload_t *payload)
{
    if (payload == nullptr || !s_has_pending_payload)
    {
        return false;
    }

    *payload = s_pending_payload;
    s_pending_payload = {};
    s_has_pending_payload = false;
    return true;
}

esp_err_t provisioning_transport_submit_ble_payload(const provisioning_payload_t *payload)
{
    if (payload == nullptr)
    {
        return ESP_ERR_INVALID_ARG;
    }

    s_pending_payload = {};
    copy_string(s_pending_payload.config.device_id, sizeof(s_pending_payload.config.device_id), payload->config.device_id);
    copy_string(s_pending_payload.config.ssid, sizeof(s_pending_payload.config.ssid), payload->config.ssid);
    copy_string(s_pending_payload.config.password, sizeof(s_pending_payload.config.password), payload->config.password);
    copy_string(s_pending_payload.config.mqtt_uri, sizeof(s_pending_payload.config.mqtt_uri), payload->config.mqtt_uri);

    s_pending_payload.has_token = payload->has_token;
    if (payload->has_token)
    {
        copy_string(s_pending_payload.token, sizeof(s_pending_payload.token), payload->token);
    }

    s_has_pending_payload = true;
    ESP_LOGI(TAG, "Provisioning payload submitted through BLE transport seam");
    set_status("ok:accepted");
    return ESP_OK;
}
