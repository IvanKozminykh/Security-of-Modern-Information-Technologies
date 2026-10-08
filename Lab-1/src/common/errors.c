#include "errors.h"

#include <stdio.h>

const wchar_t* ErrorText(DWORD code, wchar_t* buf, DWORD cch)
{
    DWORD flags = FORMAT_MESSAGE_FROM_SYSTEM | FORMAT_MESSAGE_IGNORE_INSERTS;
    DWORD len = FormatMessageW(flags, NULL, code, 0, buf, cch, NULL);

    while (len > 0) {
        wchar_t last = buf[len - 1];
        if (last != L'\r' && last != L'\n' && last != L'.')
            break;
        len--;
        buf[len] = L'\0';
    }
    if (len == 0)
        swprintf_s(buf, cch, L"код %lu", code);
    return buf;
}
