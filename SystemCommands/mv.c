#include <stdio.h>

int main(void)
{
    if (argc() != 3)
    {
        printf("mv <source> <destination>\n");
        return 1;
    }

    move(argv(1), argv(2));
    return 0;
}
