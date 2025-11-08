#include <stdio.h>

int main(void)
{
    if (argc() < 2)
    {
        printf("cat <path> [more paths]\n");
        return 1;
    }

    int count = argc();
    int index = 1;
    while (index < count)
    {
        char* path = argv(index);
        if (!exists(path))
        {
            printf("cat: %s not found\n", path);
        }
        else
        {
            char* content = readall(path);
            printf("%s", content);
            if (index < count - 1)
            {
                printf("\n");
            }
        }
        index = index + 1;
    }
    return 0;
}
