#include "console.h"

#include <windows.h>
#include <fcntl.h>
#include <io.h>
#include <stdio.h>

static void SetStreamMode(FILE* stream, DWORD stdHandle)
{
    DWORD type = GetFileType(GetStdHandle(stdHandle));

    if (type == FILE_TYPE_CHAR)
        _setmode(_fileno(stream), _O_U16TEXT);
    else
        _setmode(_fileno(stream), _O_U8TEXT);
}

void ConsoleInit(void)
{
    SetStreamMode(stdin, STD_INPUT_HANDLE);
    SetStreamMode(stdout, STD_OUTPUT_HANDLE);
    SetStreamMode(stderr, STD_ERROR_HANDLE);
}
