#pragma once

#include <windows.h>

void TokenAccountName(HANDLE token, WCHAR* out, size_t cch);

void DescribeToken(HANDLE token, WCHAR* out, size_t cch);

void DescribeProcess(WCHAR* out, size_t cch);

void DescribeThread(WCHAR* out, size_t cch);
