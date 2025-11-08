#include <stdio.h>

char* join_path(char* base, char* leaf)
{
    if (startswith(leaf, "/")) return leaf;
    int len = strlen(base);
    if (len == 0) return leaf;
    if (strchar(base, len - 1) == '/')
    {
        return strcat(base, leaf);
    }
    return strcat(strcat(base, "/"), leaf);
}

char* get_basename(char* path)
{
    int len = strlen(path);
    if (len == 0) return path;
    int index = len - 1;
    while (index >= 0)
    {
        if (strchar(path, index) == '/')
        {
            return substr(path, index + 1, len - index - 1);
        }
        index = index - 1;
    }
    return path;
}

int is_directory(char* path)
{
char* info = malloc(32);
    stat(path, info);
    int exists = load32(info, 0);
    int dir = load32(info, 4);
    free(info);
    return exists && dir;
}

int main(void)
{
    if (argc() != 3)
    {
        printf("mv <source> <destination>\n");
        return 1;
    }

    char* source = argv(1);
    char* destination = argv(2);
    if (!exists(source))
    {
        printf("mv: %s not found\n", source);
        return 1;
    }

    if (is_directory(destination))
    {
        destination = join_path(destination, get_basename(source));
    }

    rename(source, destination);
    return 0;
}
