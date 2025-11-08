/*
 * MiniOS builtin declarations and helper prototypes.
 * The MiniC runtime ignores argument counts for builtins, but these
 * declarations make shared APIs visible to user programs.
 */

int printf(char* fmt);
int puts(char* text);
int putchar(int ch);
int getchar(void);

int argc(void);
char* argv(int index);

char* malloc(int size);
int free(char* ptr);
char* memset(char* ptr, int value, int count);
char* memcpy(char* dst, char* src, int count);
int load32(char* ptr, int offset);
char* store32(char* ptr, int offset, int value);

int open(char* path, int flags);
int close(int fd);
int read(int fd, char* buffer, int count);
int write(int fd, char* buffer, int count);
int seek(int fd, int offset, int origin);

int stat(char* path, char* info);
int opendir(char* path);
int readdir(int dir, char* entryBuffer);
int rewinddir(int dir);

int dir_count(char* path);
char* dir_name(char* path, int index);
int dir_is_dir(char* path, int index);
int dir_size(char* path, int index);

int mkdir(char* path);
int remove(char* path);
int unlink(char* path);
int rename(char* oldPath, char* newPath);
int exists(char* path);
int isdir(char* path);
int filesize(char* path);

char* cwd(void);
int chdir(char* path);

int sleep_ms(int ms);
int clock_ms(void);

char* readall(char* path);
int writeall(char* path, char* text);
char* readln(void);
/* readkey returns ASCII for printable input or a special code (see keycode). */
int readkey(void);
char* input(char* prompt);
void console_clear(void);
void console_set_cursor(int col, int row);
int console_cursor_col(void);
int console_cursor_row(void);
int console_width(void);
int console_height(void);
void console_show_cursor(int visible);
/* keycode("up"), keycode("down"), etc expose special key values for readkey. */
int keycode(char* name);

int spawn(char* path);
int wait(int pid);

int proc_count(void);
int proc_pid(int index);
char* proc_name(int index);
char* proc_state(int index);
int proc_mem(int index);
int proc_kill(int pid);

int strlen(char* text);
int strchar(char* text, int index);
char* substr(char* text, int start, int length);
char* strcat(char* a, char* b);
int startswith(char* value, char* prefix);
