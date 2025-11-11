#include <stdio.h>

int FLAG_WRITE  = 2;
int FLAG_CREATE = 4;
int FLAG_TRUNC  = 8;

char* make_char(int ch)
{
    char* buf = malloc(2);
    buf[0] = ch;
    buf[1] = 0;
    return buf;
}

char* strip_outer_quotes(char* s)
{
    int len = strlen(s);
    if (len >= 2 && strchar(s, 0) == '\"' && strchar(s, len - 1) == '\"')
    {
        return substr(s, 1, len - 2);
    }
    return s;
}

int hex_val(int ch)
{
    if (ch >= '0' && ch <= '9') return ch - '0';
    if (ch >= 'a' && ch <= 'f') return ch - 'a' + 10;
    if (ch >= 'A' && ch <= 'F') return ch - 'A' + 10;
    return -1;
}

char* decode_url(char* s)
{
    char* out = "";
    int i = 0;
    int len = strlen(s);
    while (i < len)
    {
        int c = strchar(s, i);
        if (c == '%')
        {
            if (i + 2 < len)
            {
                int h1 = strchar(s, i + 1);
                int h2 = strchar(s, i + 2);
                int v1 = hex_val(h1);
                int v2 = hex_val(h2);
                if (v1 >= 0 && v2 >= 0)
                {
                    char* t = make_char(v1 * 16 + v2);
                    out = strcat(out, t);
                    free(t);
                    i = i + 3;
                    continue;
                }
            }
            {
                char* t = make_char('%');
                out = strcat(out, t);
                free(t);
                i = i + 1;
            }
            continue;
        }
        if (c == '+')
        {
            char* t = make_char(' ');
            out = strcat(out, t);
            free(t);
            i = i + 1;
            continue;
        }
        {
            char* t = make_char(c);
            out = strcat(out, t);
            free(t);
            i = i + 1;
        }
    }
    return out;
}

char* join_text()
{
    char* text = "";
    int index = 2;
    while (index < argc())
    {
        char* part = argv(index);
        part = strip_outer_quotes(part);
        part = decode_url(part);

        text = strcat(text, part);
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
        printf("usage: write <path> <url-encoded-text>\n");
        printf("       ASCII URL encoding: %%HH; '+' is space. Use %%2B for '+'.\n");
        printf("e.g.   write /home/user/hello.c \"%%23include%%20%%3Cstdio.h%%3E%%0Aint%%20main(void)%%20%%7B%%0A%%20%%20printf(%%22Hello,%%20world%%5Cn%%22);%%0A%%20%%20return%%200;%%0A%%7D\"\n");
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
