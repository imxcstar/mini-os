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
    int seconds = 1;
    if (argc() > 1)
    {
        int parsed = parse_int(argv(1));
        if (parsed >= 0) seconds = parsed;
    }
    int millis = seconds * 1000;
    sleep_ms(millis);
    return 0;
}
