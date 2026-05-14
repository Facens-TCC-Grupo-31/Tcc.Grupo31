idf_component_register(
    SRCS "main.cpp"
    INCLUDE_DIRS "." "../../../Abstractions" "../../../Contacts"
    REQUIRES esp_timer esp_driver_gpio nvs_flash
)
