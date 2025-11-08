#include <stdio.h>

void print_entry(char* path, int index)
{
    char* name = dir_name(path, index);
    int kind = dir_is_dir(path, index);
    int size = dir_size(path, index);
    if (kind == 1)
    {
        printf("%s/\n", name);
    }
    else
    {
        printf("%s\t%d\n", name, size);
    }
}

int main(void)
{
    char* target = ".";
    if (argc() > 1)
    {
        target = argv(1);
    }

    int count = dir_count(target);
    int index = 0;
    while (index < count)
    {
        print_entry(target, index);
        index = index + 1;
    }
    return 0;
}
