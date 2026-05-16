#pragma once

#include "Reader.h"
#include "Gateway.h"
#include <atomic>
#include <memory>

template <typename TData>
class Feeder
{
private:
	std::unique_ptr<Reader<TData>> reader_;
	std::unique_ptr<Gateway<TData>> gateway_;
	std::atomic<bool> running_ = false;

protected:
	Reader<TData>& reader()
	{
		return *reader_;
	}

	virtual std::optional<TData> read_cycle_data()
	{
		return reader_->read();
	}

	virtual int cycle_sleep_ms() const
	{
		return 1000;
	}

	virtual void loop()
	{
		while (running_)
		{
			auto data = read_cycle_data();
			
			if (data.has_value())
			{
				gateway_->send(data.value());
			}

			sleep_ms(cycle_sleep_ms());
		}
	}

	virtual void run_async() = 0;
	virtual void sleep_ms(int ms) = 0;

public:
	Feeder(std::unique_ptr<Reader<TData>> reader, std::unique_ptr<Gateway<TData>> gateway) : reader_(std::move(reader)), gateway_(std::move(gateway)) {}
	~Feeder() = default;

	void start()
	{
		running_ = true;
		run_async();
	}

	void stop()
	{
		running_ = false;
	}
};
