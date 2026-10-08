#pragma once

#include <stddef.h>

int ReadLine(wchar_t* buf, size_t cch);

void ReadPassword(wchar_t* buf, size_t cch);

int Tokenize(wchar_t* line, wchar_t** argv, int maxArgs);
