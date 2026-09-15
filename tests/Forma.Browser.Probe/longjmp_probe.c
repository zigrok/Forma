#include <setjmp.h>

static void jump_to(jmp_buf target, int value) { longjmp(target, value); }

static void cross_invocation(jmp_buf outer)
{
    jmp_buf inner;
    if (setjmp(inner) == 0) jump_to(outer, 7);
}

int forma_probe_longjmp(void)
{
    jmp_buf first, second, outer;
    int a = setjmp(first);
    if (a == 0) {
        int b = setjmp(second);
        if (b == 0) jump_to(second, 9);
        if (b != 9) return -1;
        jump_to(first, 0);
    }
    if (a != 1) return -2;
    int c = setjmp(outer);
    if (c == 0) {
        cross_invocation(outer);
        return -3;
    }
    return c == 7 ? 1 : -4;
}
