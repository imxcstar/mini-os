#include <stdio.h>

int FLAG_READ = 1;
int FLAG_WRITE = 2;
int FLAG_CREATE = 4;
int FLAG_TRUNC = 8;

int copy_file(char* source, char* destination)
{
    int input = open(source, FLAG_READ);
    if (input < 0)
    {
        printf("cp: cannot open %s\n", source);
        return 1;
    }
    int output = open(destination, FLAG_WRITE + FLAG_CREATE + FLAG_TRUNC);
    if (output < 0)
    {
        printf("cp: cannot open %s\n", destination);
        close(input);
        return 1;
    }
    char* buffer = malloc(512);
    while (1)
    {
        int bytes = read(input, buffer, 512);
        if (bytes <= 0) break;
        write(output, buffer, bytes);
    }
    close(input);
    close(output);
    free(buffer);
    return 0;
}

int main(void)
{
    if (argc() != 3)
    {
        printf("cp <source> <destination>\n");
        return 1;
    }
    return copy_file(argv(1), argv(2));
}
