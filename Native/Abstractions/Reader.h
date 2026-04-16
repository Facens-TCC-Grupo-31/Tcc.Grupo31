#pragma once

#include <optional>

template <typename TData>
class Reader
{
	public:
		virtual ~Reader() = default;
		virtual std::optional<TData> read() = 0;
};