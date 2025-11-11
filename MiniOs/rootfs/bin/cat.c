#include <stdio.h>

int FLAG_READ = 1;

int dump_file(char* path)
{
    int fd = open(path, FLAG_READ);
    if (fd < 0)
    {
        printf("cat: cannot open %s\n", path);
        return 1;
    }
    char* buffer = malloc(512);
    while (1)
    {
        int bytes = read(fd, buffer, 512);
        if (bytes <= 0) break;
        write(1, buffer, bytes);
    }
    close(fd);
    free(buffer);
    return 0;
}

int main(void)
{
    if (argc() < 2)
    {
        printf("cat <path> [more paths]\n");
        return 1;
    }
    int index = 1;
    while (index < argc())
    {
        if (dump_file(argv(index)) != 0)
            return 1;
        index = index + 1;
        if (index < argc())
            printf("\n");
    }
    return 0;
}
