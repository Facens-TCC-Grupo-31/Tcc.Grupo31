#pragma once

#include "Feeder.h"

#include <chrono>
#include <thread>

template <typename TData>
class ServerFeeder : public Feeder<TData>
{
private:
	std::thread thread_;

public:
	using Feeder<TData>::Feeder;

	~ServerFeeder() override
	{
		this->stop();

		if (thread_.joinable())
		{
			thread_.join();
		}
	}

protected:
	void run_async() override
	{
		thread_ = std::thread([this]()
		{
			this->loop();
		});
	}

	void sleep_ms(int ms) override
	{
		std::this_thread::sleep_for(std::chrono::milliseconds(ms));
	}
};
