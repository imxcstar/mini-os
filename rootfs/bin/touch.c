#include <stdio.h>

int main(void)
{
    if (argc() < 2)
    {
        printf("touch <path>\n");
        return 1;
    }

    int index = 1;
    while (index < argc())
    {
        writeall(argv(index), "");
        index = index + 1;
    }
    return 0;
}
