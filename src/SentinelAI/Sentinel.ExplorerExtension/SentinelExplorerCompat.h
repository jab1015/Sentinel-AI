#pragma once

#include <algorithm>

// Sentinel builds with NOMINMAX so Windows headers cannot inject min/max macros.
// Keep unqualified legacy call sites bound to the C++ standard algorithm instead.
using std::min;
