#pragma once

#include "Feeder.h"
#include "freertos/FreeRTOS.h"
#include "freertos/task.h"

template <typename TData>
class EspFeeder : public Feeder<TData>
{
protected:
	static void task_entry(void* param)
	{
		auto self = static_cast<EspFeeder*>(param);
		self->loop();
	}

	void run_async() override
	{
		xTaskCreate(task_entry, "FeederTask", 4096, this, 1, nullptr);
	}

	void sleep_ms(int ms) override
	{
		vTaskDelay(pdMS_TO_TICKS(ms));
	}

public:
	using Feeder<TData>::Feeder;
};