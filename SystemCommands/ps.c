#include <stdio.h>

int main(void)
{
    printf("PID\tSTATE\tNAME\n");
    printf("%s", ps());
    return 0;
}
