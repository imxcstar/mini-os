#include <stdio.h>

int FLAG_WRITE = 2;
int FLAG_CREATE = 4;
int FLAG_TRUNC = 8;

char* join_text()
{
    char* text = "";
    int index = 2;
    while (index < argc())
    {
        text = strcat(text, argv(index));
        if (index < argc() - 1)
        {
            text = strcat(text, " ");
        }
        index = index + 1;
    }
    return text;
}

int main(void)
{
    if (argc() < 3)
    {
        printf("write <path> <text>\n");
        return 1;
    }
    char* path = argv(1);
    char* payload = join_text();
    int fd = open(path, FLAG_WRITE + FLAG_CREATE + FLAG_TRUNC);
    if (fd < 0)
    {
        printf("write: cannot open %s\n", path);
        return 1;
    }
    write(fd, payload, strlen(payload));
    close(fd);
    return 0;
}
