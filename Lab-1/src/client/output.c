#include "output.h"

#include <stdio.h>

#include "errors.h"

void PrintError(const wchar_t* what, long code)
{
    WCHAR msg[256];
    wprintf(L"  %s: ошибка %ld - %s\n", what, code, ErrorText((DWORD)code, msg, ARRAYSIZE(msg)));
}

void PrintProgress(ULONGLONG done, ULONGLONG total, ULONGLONG startTick)
{
    static ULONGLONG lastTick;
    ULONGLONG now = GetTickCount64();

    if (done < total && now - lastTick < 250)
        return;
    lastTick = now;

    double seconds = (now - startTick) / 1000.0;
    double doneMb = done / 1048576.0;
    double totalMb = total / 1048576.0;
    unsigned percent = 100;
    double speed = 0.0;

    if (total > 0)
        percent = (unsigned)(done * 100 / total);
    if (seconds > 0)
        speed = doneMb / seconds;

    wprintf(L"\r  %.1f / %.1f МБ (%3u%%), %.1f МБ/с   ", doneMb, totalMb, percent, speed);
    fflush(stdout);
}
