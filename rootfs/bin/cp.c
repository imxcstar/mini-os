#include <stdio.h>

char* join_path(char* base, char* leaf)
{
    if (startswith(leaf, "/")) return leaf;
    if (strlen(base) == 0) return leaf;
    if (strchar(base, strlen(base) - 1) == '/')
    {
        return strcat(base, leaf);
    }
    return strcat(strcat(base, "/"), leaf);
}

void copy_tree(char* source, char* destination)
{
    if (isdir(source))
    {
        mkdir(destination);
        int count = dir_count(source);
        int index = 0;
        while (index < count)
        {
            char* child = dir_name(source, index);
            copy_tree(join_path(source, child), join_path(destination, child));
            index = index + 1;
        }
        return;
    }

    char* payload = readall(source);
    writeall(destination, payload);
}

int main(void)
{
    if (argc() != 3)
    {
        printf("cp <source> <destination>\n");
        return 1;
    }

    char* source = argv(1);
    char* destination = argv(2);

    if (!exists(source))
    {
        printf("cp: %s not found\n", source);
        return 1;
    }

    if (exists(destination))
    {
        printf("cp: %s already exists\n", destination);
        return 1;
    }

    copy_tree(source, destination);
    return 0;
}
