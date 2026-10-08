#pragma once

#include <windows.h>

#define RPC_CALL(result, call) \
    RpcTryExcept { \
        (result) = (call); \
    } \
    RpcExcept(RpcExceptionFilter(RpcExceptionCode())) { \
        (result) = (long)RpcExceptionCode(); \
    } \
    RpcEndExcept
