#pragma once

#include <iostream>
#include <memory>
#include "Reader.h"
#include "MqttGateway.h"
#include "Feeder.h"

class MockReader : public Reader<int>
{
public:
	std::optional<int> read() override
	{
		static int counter = 0;
		return counter++;
	}
};

int main()
{
	std::cout << "Starting Feeder application..." << std::endl;
	auto reader = std::make_unique<MockReader>();
	auto gateway = std::make_unique<MqttGateway<int>>("tcp://localhost:1883", "test/topic", "a283ac8d-9d19-4030-b972-1d5d91162628");

	Feeder<int> feeder(std::move(reader), std::move(gateway));
	feeder.start();
}
