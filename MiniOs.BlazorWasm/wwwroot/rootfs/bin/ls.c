#include <stdio.h>

int list_dir(char* path)
{
    char* entry = malloc(256);
    int dir = opendir(path);
    if (dir < 0)
    {
        printf("ls: cannot open %s\n", path);
        return 1;
    }
    while (readdir(dir, entry))
    {
        int is_dir = load32(entry, 0);
        int size = load32(entry, 4);
        char* name = entry + 8;
        if (is_dir)
        {
            printf("%s/\n", name);
        }
        else
        {
            printf("%s\t%d\n", name, size);
        }
    }
    free(entry);
    return 0;
}

int main(void)
{
    char* target = ".";
    if (argc() > 1)
    {
        target = argv(1);
    }
    return list_dir(target);
}
