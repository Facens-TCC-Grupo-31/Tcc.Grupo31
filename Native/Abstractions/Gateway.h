#pragma once

template <typename TData>
class Gateway
{
	public:
		virtual ~Gateway() = default;
		virtual void send(const TData& data) = 0;
};