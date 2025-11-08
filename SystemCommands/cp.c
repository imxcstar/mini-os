#include <stdio.h>

int main(void)
{
    if (argc() != 3)
    {
        printf("cp <source> <destination>\n");
        return 1;
    }

    copy(argv(1), argv(2));
    return 0;
}
