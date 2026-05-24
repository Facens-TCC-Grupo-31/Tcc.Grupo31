#pragma once

#include "Feeder.h"
#include "freertos/FreeRTOS.h"
#include "freertos/task.h"

template <typename TData>
class EspFeeder : public Feeder<TData>
{
private:
	std::atomic<TaskHandle_t> task_handle_ = nullptr;

protected:
	void wait_for_task_exit()
	{
		while (task_handle_.load() != nullptr)
		{
			vTaskDelay(pdMS_TO_TICKS(10));
		}
	}

protected:
	static void task_entry(void* param)
	{
		auto self = static_cast<EspFeeder*>(param);
		self->loop();
		self->task_handle_.store(nullptr);
		vTaskDelete(nullptr);
	}

	void run_async() override
	{
		TaskHandle_t handle = nullptr;
		if (xTaskCreate(task_entry, "FeederTask", 4096, this, 1, &handle) == pdPASS)
		{
			task_handle_.store(handle);
			return;
		}

		this->stop();
	}

	void sleep_ms(int ms) override
	{
		vTaskDelay(pdMS_TO_TICKS(ms));
	}

public:
	using Feeder<TData>::Feeder;

	~EspFeeder() override
	{
		this->stop();
		wait_for_task_exit();
	}
};