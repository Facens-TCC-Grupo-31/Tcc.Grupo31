#pragma once
#pragma once

#include <memory>
#include <thread>

template <typename TData>
class ServerFeeder : public Feeder<TData>
{
private:
	std::thread thread_;

public:
	using Feeder<TData>::Feeder;

	~ServerFeeder()
	{
		this->stop();

		if (thread_.joinable)
		{
			thread_.join();
		}
	}

protected:
	void run_async() override
	{
		thread_ = std::thread(&Feeder::loop, this);
	}

	void sleep_ms(int ms) override
	{
		std::this_thread::sleep_for(std::chrono::milliseconds(1000));
	}
};
