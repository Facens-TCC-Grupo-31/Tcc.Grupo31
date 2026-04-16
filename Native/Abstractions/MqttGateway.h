#pragma once

#include "Gateway.h"
#include <string>
#include <mqtt/async_client.h>

template <typename TData>
class MqttGateway : public Gateway<TData>
{
private:
	mqtt::async_client client_;

	std::string broker_uri_;
	std::string topic_;

	std::string client_id_;

	std::string serialize(const TData& data)
	{
		return std::to_string(data);
	}

public:
	MqttGateway(
		const std::string& brokerUri,
		const std::string& topic,
		const std::string& clientId
	) : broker_uri_(brokerUri), topic_(topic), client_id_(clientId), client_(brokerUri, clientId)
	{
		mqtt::connect_options opts;
		opts.set_automatic_reconnect(true);
		client_.connect(opts)->wait();
		std::cout << "Initialized MQTT Gateway with broker: " << broker_uri_ << " and topic: " << topic_ << std::endl;
	}

	void send(const TData& data) override
	{
		std::cout << "Sending data to MQTT broker: " << broker_uri_ << " on topic: " << topic_ << std::endl;
		std::cout << "Data: " << data << std::endl;
		
		std::string payload = serialize(data);
		client_.publish(topic_, payload.c_str(), payload.size(), 1, false)->wait();
	}
};