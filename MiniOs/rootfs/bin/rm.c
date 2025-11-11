#include <stdio.h>

int main(void)
{
    if (argc() < 2)
    {
        printf("rm <path>\n");
        return 1;
    }

    int index = 1;
    while (index < argc())
    {
        remove(argv(index));
        index = index + 1;
    }
    return 0;
}
