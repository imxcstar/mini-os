#include <stdio.h>

int main(void)
{
    char* target = ".";
    if (argc() > 1)
    {
        target = argv(1);
    }
    char* listing = listdir(target);
    printf("%s", listing);
    return 0;
}
