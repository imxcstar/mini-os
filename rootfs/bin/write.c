#include <stdio.h>

char* join_text()
{
    char* result = "";
    int index = 2;
    while (index < argc())
    {
        result = strcat(result, argv(index));
        if (index < argc() - 1)
        {
            result = strcat(result, " ");
        }
        index = index + 1;
    }
    return result;
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
    writeall(path, payload);
    return 0;
}
