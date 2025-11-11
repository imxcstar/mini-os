#include <stdio.h>

int main(void)
{
    int count = argc();
    int index = 1;
    while (index < count)
    {
        printf("%s", argv(index));
        if (index < count - 1)
        {
            printf(" ");
        }
        index = index + 1;
    }
    printf("\n");
    return 0;
}
