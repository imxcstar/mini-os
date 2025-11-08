#include <stdio.h>

int main(void)
{
    printf("PID\tSTATE\tMEM\tNAME\n");
    int count = proc_count();
    int index = 0;
    while (index < count)
    {
        int pid = proc_pid(index);
        char* state = proc_state(index);
        char* name = proc_name(index);
        int memory = proc_mem(index);
        printf("%d\t%s\t%d\t%s\n", pid, state, memory, name);
        index = index + 1;
    }
    return 0;
}
