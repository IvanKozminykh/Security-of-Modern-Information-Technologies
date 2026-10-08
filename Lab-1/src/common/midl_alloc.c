#include <stdlib.h>

#include "FileManager.h"

void* __RPC_USER midl_user_allocate(size_t size)
{
    return malloc(size);
}

void __RPC_USER midl_user_free(void* ptr)
{
    free(ptr);
}
