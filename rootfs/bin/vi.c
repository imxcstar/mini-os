#include <stdio.h>

char* lines[256];
int line_count = 0;
int cursor = 0;
int dirty = 0;

int is_space(int code)
{
    return code == 32 || code == 9 || code == 13 || code == 10;
}

char* trim(char* text)
{
    int len = strlen(text);
    int start = 0;
    while (start < len && is_space(strchar(text, start))) start = start + 1;
    int end = len - 1;
    while (end >= start && is_space(strchar(text, end))) end = end - 1;
    if (end < start) return "";
    return substr(text, start, end - start + 1);
}

void reset_buffer()
{
    int i = 0;
    while (i < 256)
    {
        lines[i] = "";
        i = i + 1;
    }
    line_count = 0;
    cursor = 0;
    dirty = 0;
}

void ensure_min_line()
{
    if (line_count == 0)
    {
        lines[0] = "";
        line_count = 1;
    }
}

void insert_line(int index, char* text)
{
    if (line_count >= 256)
    {
        printf("vi: buffer full, cannot insert more lines\n");
        return;
    }
    if (index < 0) index = 0;
    if (index > line_count) index = line_count;
    int i = line_count;
    while (i > index)
    {
        lines[i] = lines[i - 1];
        i = i - 1;
    }
    lines[index] = text;
    line_count = line_count + 1;
    cursor = index;
    dirty = 1;
}

void replace_line(int index, char* text)
{
    if (index < 0 || index >= line_count) return;
    lines[index] = text;
    cursor = index;
    dirty = 1;
}

void delete_line(int index)
{
    if (index < 0 || index >= line_count) return;
    if (line_count == 1)
    {
        lines[0] = "";
        dirty = 1;
        return;
    }
    int i = index;
    while (i < line_count - 1)
    {
        lines[i] = lines[i + 1];
        i = i + 1;
    }
    line_count = line_count - 1;
    if (cursor >= line_count) cursor = line_count - 1;
    dirty = 1;
}

void list_buffer()
{
    int i = 0;
    while (i < line_count)
    {
        printf("%d\t%s\n", i + 1, lines[i]);
        i = i + 1;
    }
}

void show_current()
{
    if (cursor < 0 || cursor >= line_count) return;
    printf("%d\t%s\n", cursor + 1, lines[cursor]);
}

void load_document(char* text)
{
    reset_buffer();
    int len = strlen(text);
    int start = 0;
    while (start <= len && line_count < 256)
    {
        int pos = start;
        while (pos < len && strchar(text, pos) != 10) pos = pos + 1;
        int piece_len = pos - start;
        lines[line_count] = substr(text, start, piece_len);
        line_count = line_count + 1;
        if (pos >= len) break;
        start = pos + 1;
    }
    ensure_min_line();
    dirty = 0;
    cursor = 0;
}

void load_file(char* filename)
{
    if (exists(filename))
    {
        char* contents = readall(filename);
        load_document(contents);
        printf("Opened %s (%d lines)\n", filename, line_count);
    }
    else
    {
        reset_buffer();
        ensure_min_line();
        printf("New file %s\n", filename);
    }
}

char* join_lines()
{
    char* result = "";
    int i = 0;
    while (i < line_count)
    {
        result = strcat(result, lines[i]);
        if (i < line_count - 1) result = strcat(result, "\n");
        i = i + 1;
    }
    return result;
}

char* ensure_filename(char* current)
{
    if (strlen(current) > 0) return current;
    char* fresh = trim(input("write file path: "));
    if (strlen(fresh) == 0)
    {
        printf("write cancelled\n");
        return current;
    }
    return fresh;
}

char* save_to(char* current)
{
    char* name = ensure_filename(current);
    if (strlen(name) == 0) return current;
    char* payload = join_lines();
    writeall(name, payload);
    dirty = 0;
    printf("wrote %d lines to %s\n", line_count, name);
    return name;
}

void insert_mode(int after)
{
    printf("-- insert mode (. on its own line to finish) --\n");
    int target = after;
    int replaced_head = 0;
    while (1)
    {
        char* text = input("");
        if (text == ".") break;
        if (!replaced_head && line_count == 1 && strlen(lines[0]) == 0 && after == 0)
        {
            replace_line(0, text);
            replaced_head = 1;
            target = 0;
            continue;
        }
        target = target + 1;
        insert_line(target, text);
    }
    if (cursor >= line_count) cursor = line_count - 1;
    printf("-- insert complete --\n");
}

void print_help()
{
    printf(":w save, :w <path> save as, :wq write+quit, :q quit, :q! force quit\n");
    printf(":p print buffer, :n show current line, :up/:down move cursor\n");
    printf(":i insert mode (multi-line), :r replace current line, :d delete line\n");
    printf(":e <path> open file. Any other text inserts a new line after the cursor.\n");
}

int main(void)
{
    reset_buffer();
    char* filename = trim(input("vi file path (default /home/user/vi.txt): "));
    if (strlen(filename) == 0) filename = "/home/user/vi.txt";
    load_file(filename);
    ensure_min_line();
    while (1)
    {
        char* dirty_mark = "";
        if (dirty) dirty_mark = "*";
        printf("vi:%s [%d/%d]%s> ", filename, cursor + 1, line_count, dirty_mark);
        char* command = trim(input(""));
        if (strlen(command) == 0) continue;
        if (command == ":help")
        {
            print_help();
            continue;
        }
        if (command == ":q")
        {
            if (dirty)
            {
                printf("No write since last change (:w to save, :q! to quit)\n");
                continue;
            }
            break;
        }
        if (command == ":q!")
        {
            break;
        }
        if (command == ":wq" || command == ":wq!")
        {
            filename = save_to(filename);
            if (dirty == 0) break;
            continue;
        }
        if (command == ":w")
        {
            filename = save_to(filename);
            continue;
        }
        if (startswith(command, ":w "))
        {
            char* path = trim(substr(command, 3, strlen(command) - 3));
            if (strlen(path) > 0) filename = path;
            filename = save_to(filename);
            continue;
        }
        if (startswith(command, ":e "))
        {
            char* path = trim(substr(command, 3, strlen(command) - 3));
            if (strlen(path) > 0)
            {
                filename = path;
                load_file(filename);
            }
            continue;
        }
        if (command == ":p")
        {
            list_buffer();
            continue;
        }
        if (command == ":n")
        {
            show_current();
            continue;
        }
        if (command == ":up")
        {
            if (cursor > 0) cursor = cursor - 1;
            continue;
        }
        if (command == ":down")
        {
            if (cursor < line_count - 1) cursor = cursor + 1;
            continue;
        }
        if (command == ":i")
        {
            insert_mode(cursor);
            continue;
        }
        if (command == ":r")
        {
            char* text = input("replace> ");
            replace_line(cursor, text);
            continue;
        }
        if (command == ":d")
        {
            delete_line(cursor);
            ensure_min_line();
            continue;
        }
        if (command == ":append")
        {
            insert_mode(line_count - 1);
            continue;
        }
        if (strchar(command, 0) == ':')
        {
            printf("Unknown command: %s\n", command);
            continue;
        }
        insert_line(cursor + 1, command);
    }
    printf("bye\n");
    return 0;
}
