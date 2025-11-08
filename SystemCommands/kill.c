#include <stdio.h>

int parse_int(char* text)
{
    int len = strlen(text);
    if (len == 0) return -1;
    int value = 0;
    int index = 0;
    while (index < len)
    {
        int digit = strchar(text, index) - 48;
        if (digit < 0 || digit > 9) return -1;
        value = value * 10 + digit;
        index = index + 1;
    }
    return value;
}

int main(void)
{
    if (argc() != 2)
    {
        printf("kill <pid>\n");
        return 1;
    }

    int pid = parse_int(argv(1));
    if (pid < 0)
    {
        printf("kill: invalid pid\n");
        return 1;
    }

    int rc = killproc(pid);
    if (rc == 0)
    {
        return 0;
    }

    printf("kill: no such pid %d\n", pid);
    return 1;
}
