/* Isolated SDL-shaped ABI fixture: never links SDL or accesses an OS clipboard. */
#include <stdbool.h>
#include <stdint.h>
#include <stdlib.h>
#include <string.h>

#ifdef _WIN32
#define EXPORT __declspec(dllexport)
#else
#define EXPORT __attribute__((visibility("default")))
#endif

static int result;
static int free_count;
static char text[1024];

static bool store_text(const char *value)
{
    if (!value) return false;
    size_t length = strlen(value);
    if (length >= sizeof(text)) return false;
    memcpy(text, value, length + 1);
    return true;
}

EXPORT void fixture_set_result(int value) { result = value; }
EXPORT int fixture_free_count(void) { return free_count; }
EXPORT uint32_t SDL_WasInit(uint32_t flags) { return flags; }
EXPORT void SDL_ClearError(void) { }
EXPORT const char *SDL_GetError(void) { return ""; }
EXPORT int fixture_set2(const char *value)
{
    if (!store_text(value)) return -1;
    return result;
}
EXPORT bool fixture_set3(const char *value)
{
    if (!store_text(value)) return false;
    return result != 0;
}
EXPORT char *SDL_GetClipboardText(void)
{
    size_t length = strlen(text) + 1;
    char *copy = malloc(length);
    if (copy) memcpy(copy, text, length);
    return copy;
}
EXPORT void SDL_free(void *pointer)
{
    free_count++;
    free(pointer);
}
