#include <stdio.h>

int FLAG_WRITE = 2;
int FLAG_CREATE = 4;

int main(void)
{
    if (argc() < 2)
    {
        printf("touch <path>\n");
        return 1;
    }
    int fd = open(argv(1), FLAG_WRITE + FLAG_CREATE);
    if (fd < 0)
    {
        printf("touch: cannot update %s\n", argv(1));
        return 1;
    }
    close(fd);
    return 0;
}
