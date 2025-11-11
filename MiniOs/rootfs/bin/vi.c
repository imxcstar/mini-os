#include <stdio.h>

int MAX_LINE_COUNT = 512;
char* lines[512];

int line_count = 0;
int cursor_line = 0;
int cursor_col = 0;
int viewport_top = 0;
int viewport_left = 0;
int dirty = 0;
int running = 1;

int MODE_NORMAL = 0;
int MODE_INSERT = 1;
int MODE_COMMAND = 2;
int mode = 0;

int screen_rows = 24;
int screen_cols = 80;
int body_rows = 22;
int content_width = 72;

int pending_delete = 0;

char* status_message = "Press :help for commands";
char* command_buffer = "";
char* filename = "";

int KEY_UP = 0;
int KEY_DOWN = 0;
int KEY_LEFT = 0;
int KEY_RIGHT = 0;
int KEY_DELETE = 0;
int KEY_ENTER = 10;
int KEY_ESCAPE = 27;
int KEY_BACKSPACE = 8;
int KEY_HOME = 0;
int KEY_END = 0;
int KEY_PAGEUP = 0;
int KEY_PAGEDOWN = 0;
int KEY_TAB = 9;

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

void set_status(char* text)
{
    status_message = text;
}

void reset_buffer()
{
    int i = 0;
    while (i < MAX_LINE_COUNT)
    {
        lines[i] = "";
        i = i + 1;
    }
    line_count = 0;
}

void ensure_min_line()
{
    if (line_count == 0)
    {
        lines[0] = "";
        line_count = 1;
    }
}

int line_length(int index)
{
    if (index < 0 || index >= line_count) return 0;
    return strlen(lines[index]);
}

void clamp_cursor()
{
    if (line_count == 0)
    {
        cursor_line = 0;
        cursor_col = 0;
        return;
    }
    if (cursor_line < 0) cursor_line = 0;
    if (cursor_line >= line_count) cursor_line = line_count - 1;
    if (cursor_line < 0) cursor_line = 0;
    int len = line_length(cursor_line);
    if (cursor_col < 0) cursor_col = 0;
    if (cursor_col > len) cursor_col = len;
}

char* make_char(int key)
{
    char* buf = malloc(2);
    buf[0] = key;
    buf[1] = 0;
    return buf;
}

void load_document(char* text)
{
    reset_buffer();
    int len = strlen(text);
    int start = 0;
    while (start <= len && line_count < MAX_LINE_COUNT)
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
    cursor_line = 0;
    cursor_col = 0;
    viewport_top = 0;
    viewport_left = 0;
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

int insert_line(int index, char* text)
{
    if (line_count >= MAX_LINE_COUNT)
    {
        set_status("buffer full");
        return 0;
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
    dirty = 1;
    return 1;
}

void delete_line(int index)
{
    if (index < 0 || index >= line_count) return;
    if (line_count == 1)
    {
        lines[0] = "";
        cursor_line = 0;
        cursor_col = 0;
        dirty = 1;
        return;
    }
    while (index < line_count - 1)
    {
        lines[index] = lines[index + 1];
        index = index + 1;
    }
    line_count = line_count - 1;
    if (cursor_line >= line_count) cursor_line = line_count - 1;
    dirty = 1;
}

void insert_char(int key)
{
    char* line = lines[cursor_line];
    int len = line_length(cursor_line);
    if (cursor_col > len) cursor_col = len;
    char* left = substr(line, 0, cursor_col);
    char* right = substr(line, cursor_col, len - cursor_col);
    char* single = make_char(key);
    char* merged = strcat(left, single);
    merged = strcat(merged, right);
    free(single);
    lines[cursor_line] = merged;
    cursor_col = cursor_col + 1;
    dirty = 1;
}

void insert_newline()
{
    char* line = lines[cursor_line];
    int len = line_length(cursor_line);
    if (cursor_col > len) cursor_col = len;
    char* left = substr(line, 0, cursor_col);
    char* right = substr(line, cursor_col, len - cursor_col);
    lines[cursor_line] = left;
    if (insert_line(cursor_line + 1, right))
    {
        cursor_line = cursor_line + 1;
        cursor_col = 0;
    }
}

void backspace_char()
{
    int len = line_length(cursor_line);
    if (cursor_col > len) cursor_col = len;
    if (cursor_col > 0)
    {
        char* line = lines[cursor_line];
        char* left = substr(line, 0, cursor_col - 1);
        char* right = substr(line, cursor_col, len - cursor_col);
        lines[cursor_line] = strcat(left, right);
        cursor_col = cursor_col - 1;
        dirty = 1;
        return;
    }
    if (cursor_line == 0) return;
    int prev_len = line_length(cursor_line - 1);
    lines[cursor_line - 1] = strcat(lines[cursor_line - 1], lines[cursor_line]);
    delete_line(cursor_line);
    cursor_line = cursor_line - 1;
    cursor_col = prev_len;
}

void delete_char_forward()
{
    char* line = lines[cursor_line];
    int len = line_length(cursor_line);
    if (cursor_col > len) cursor_col = len;
    if (len == 0 && cursor_line < line_count - 1)
    {
        lines[cursor_line] = strcat(lines[cursor_line], lines[cursor_line + 1]);
        delete_line(cursor_line + 1);
        dirty = 1;
        return;
    }
    if (cursor_col >= len)
    {
        if (cursor_line < line_count - 1)
        {
            lines[cursor_line] = strcat(line, lines[cursor_line + 1]);
            delete_line(cursor_line + 1);
            dirty = 1;
        }
        return;
    }
    char* left = substr(line, 0, cursor_col);
    char* right = substr(line, cursor_col + 1, len - cursor_col - 1);
    lines[cursor_line] = strcat(left, right);
    dirty = 1;
}

void delete_current_line()
{
    delete_line(cursor_line);
    ensure_min_line();
    if (cursor_line >= line_count) cursor_line = line_count - 1;
    cursor_col = 0;
    set_status("line deleted");
}

void move_left()
{
    if (cursor_col > 0)
    {
        cursor_col = cursor_col - 1;
    }
    else if (cursor_line > 0)
    {
        cursor_line = cursor_line - 1;
        cursor_col = line_length(cursor_line);
    }
}

void move_right()
{
    int len = line_length(cursor_line);
    if (cursor_col < len)
    {
        cursor_col = cursor_col + 1;
    }
    else if (cursor_line < line_count - 1)
    {
        cursor_line = cursor_line + 1;
        cursor_col = 0;
    }
}

void move_up()
{
    if (cursor_line > 0)
    {
        cursor_line = cursor_line - 1;
        int len = line_length(cursor_line);
        if (cursor_col > len) cursor_col = len;
    }
}

void move_down()
{
    if (cursor_line < line_count - 1)
    {
        cursor_line = cursor_line + 1;
        int len = line_length(cursor_line);
        if (cursor_col > len) cursor_col = len;
    }
}

void move_home()
{
    cursor_col = 0;
}

void move_end()
{
    cursor_col = line_length(cursor_line);
}

void page_up()
{
    int step = body_rows;
    if (step < 1) step = 1;
    cursor_line = cursor_line - step;
    if (cursor_line < 0) cursor_line = 0;
    clamp_cursor();
}

void page_down()
{
    int step = body_rows;
    if (step < 1) step = 1;
    cursor_line = cursor_line + step;
    if (cursor_line >= line_count) cursor_line = line_count - 1;
    clamp_cursor();
}

void enter_insert_mode()
{
    mode = MODE_INSERT;
    pending_delete = 0;
    set_status("-- INSERT --");
}

void exit_insert_mode()
{
    mode = MODE_NORMAL;
    command_buffer = "";
    pending_delete = 0;
    if (cursor_col > 0) cursor_col = cursor_col - 1;
    clamp_cursor();
    set_status("");
}

void start_command_mode()
{
    mode = MODE_COMMAND;
    command_buffer = "";
    pending_delete = 0;
}

void cancel_command_mode()
{
    mode = MODE_NORMAL;
    command_buffer = "";
    set_status("command cancelled");
}

void append_command_char(int key)
{
    char* single = make_char(key);
    command_buffer = strcat(command_buffer, single);
    free(single);
}

void command_backspace()
{
    int len = strlen(command_buffer);
    if (len == 0) return;
    command_buffer = substr(command_buffer, 0, len - 1);
}

void open_document(char* path)
{
    filename = path;
    if (exists(path))
    {
        char* contents = readall(path);
        load_document(contents);
        set_status("opened file");
    }
    else
    {
        reset_buffer();
        ensure_min_line();
        dirty = 0;
        cursor_line = 0;
        cursor_col = 0;
        viewport_top = 0;
        viewport_left = 0;
        set_status("new file");
    }
}

int save_to(char* path)
{
    if (strlen(path) == 0)
    {
        set_status("No file name");
        return 0;
    }
    char* payload = join_lines();
    writeall(path, payload);
    dirty = 0;
    set_status("file written");
    return 1;
}

void update_screen_metrics()
{
    screen_rows = console_height();
    screen_cols = console_width();
    if (screen_rows < 4) screen_rows = 4;
    if (screen_cols < 10) screen_cols = 10;
    body_rows = screen_rows - 2;
    if (body_rows < 1) body_rows = 1;
    content_width = screen_cols - 6;
    if (content_width < 8) content_width = 8;
}

void adjust_viewport()
{
    if (cursor_line < viewport_top) viewport_top = cursor_line;
    if (cursor_line >= viewport_top + body_rows)
        viewport_top = cursor_line - body_rows + 1;
    if (viewport_top < 0) viewport_top = 0;
    if (viewport_top > line_count - 1) viewport_top = line_count - 1;
    if (viewport_top < 0) viewport_top = 0;

    if (cursor_col < viewport_left) viewport_left = cursor_col;
    if (cursor_col >= viewport_left + content_width)
        viewport_left = cursor_col - content_width + 1;
    if (viewport_left < 0) viewport_left = 0;
}

void print_line_number(int number)
{
    if (number < 10)
    {
        printf("   %d", number);
        return;
    }
    if (number < 100)
    {
        printf("  %d", number);
        return;
    }
    if (number < 1000)
    {
        printf(" %d", number);
        return;
    }
    printf("%d", number);
}

void draw_body_line(int row, int line_index)
{
    console_set_cursor(0, row);
    if (line_index >= line_count)
    {
        printf("~");
        return;
    }
    char marker = ' ';
    if (line_index == cursor_line) marker = '>';
    char* visible = substr(lines[line_index], viewport_left, content_width);
    printf("%c", marker);
    print_line_number(line_index + 1);
    printf(" %s", visible);
}

void render()
{
    clamp_cursor();
    update_screen_metrics();
    adjust_viewport();
    console_show_cursor(0);
    console_clear();

    int row = 0;
    while (row < body_rows)
    {
        draw_body_line(row, viewport_top + row);
        row = row + 1;
    }

    char* mode_label = "-- NORMAL --";
    if (mode == MODE_INSERT) mode_label = "-- INSERT --";
    else if (mode == MODE_COMMAND) mode_label = "-- COMMAND --";
    char* file_label = filename;
    if (strlen(file_label) == 0) file_label = "[No Name]";
    console_set_cursor(0, body_rows);
    if (dirty)
        printf("%s %s*  (%d/%d) col %d", mode_label, file_label, cursor_line + 1, line_count, cursor_col + 1);
    else
        printf("%s %s  (%d/%d) col %d", mode_label, file_label, cursor_line + 1, line_count, cursor_col + 1);

    console_set_cursor(0, body_rows + 1);
    if (mode == MODE_COMMAND)
    {
        printf(":%s", command_buffer);
    }
    else
    {
        printf("%s", status_message);
    }

    int cursor_screen_row = cursor_line - viewport_top;
    if (cursor_screen_row < 0) cursor_screen_row = 0;
    if (cursor_screen_row >= body_rows) cursor_screen_row = body_rows - 1;
    int cursor_screen_col = 6 + (cursor_col - viewport_left);
    if (cursor_screen_col < 6) cursor_screen_col = 6;
    if (cursor_screen_col >= screen_cols) cursor_screen_col = screen_cols - 1;
    console_set_cursor(cursor_screen_col, cursor_screen_row);
    console_show_cursor(1);
}

void execute_command()
{
    char* command = trim(command_buffer);
    if (strlen(command) == 0)
    {
        set_status("");
        mode = MODE_NORMAL;
        command_buffer = "";
        return;
    }
    if (command == "help")
    {
        set_status("Commands: :w, :q, :wq, :e <file>, ESC to cancel");
        mode = MODE_NORMAL;
        command_buffer = "";
        return;
    }
    if (command == "q")
    {
        if (dirty)
        {
            set_status("No write since last change (use :q!)");
        }
        else
        {
            running = 0;
        }
        mode = MODE_NORMAL;
        command_buffer = "";
        return;
    }
    if (command == "q!")
    {
        running = 0;
        mode = MODE_NORMAL;
        command_buffer = "";
        return;
    }
    if (command == "w")
    {
        if (strlen(filename) == 0)
            set_status("Specify file name with :w <path>");
        else if (save_to(filename)) { }
        mode = MODE_NORMAL;
        command_buffer = "";
        return;
    }
    if (command == "wq" || command == "wq!")
    {
        char* target = filename;
        if (strlen(target) == 0)
        {
            set_status("Specify file name first");
        }
        else if (save_to(target))
        {
            running = 0;
        }
        mode = MODE_NORMAL;
        command_buffer = "";
        return;
    }
    if (startswith(command, "w "))
    {
        char* path = trim(substr(command, 2, strlen(command) - 2));
        if (strlen(path) > 0)
        {
            filename = path;
            save_to(filename);
        }
        else
        {
            set_status("No file name provided");
        }
        mode = MODE_NORMAL;
        command_buffer = "";
        return;
    }
    if (startswith(command, "e "))
    {
        char* path = trim(substr(command, 2, strlen(command) - 2));
        if (strlen(path) > 0)
        {
            open_document(path);
        }
        else
        {
            set_status("No file path provided");
        }
        mode = MODE_NORMAL;
        command_buffer = "";
        return;
    }
    set_status("Unknown command");
    mode = MODE_NORMAL;
    command_buffer = "";
}

void handle_normal_key(int key)
{
    if (pending_delete)
    {
        if (key == 'd')
        {
            delete_current_line();
            pending_delete = 0;
            return;
        }
        pending_delete = 0;
    }

    if (key == 'h' || key == KEY_LEFT) { move_left(); return; }
    if (key == 'l' || key == KEY_RIGHT) { move_right(); return; }
    if (key == 'j' || key == KEY_DOWN) { move_down(); return; }
    if (key == 'k' || key == KEY_UP) { move_up(); return; }
    if (key == '0' || key == KEY_HOME) { move_home(); return; }
    if (key == '$' || key == KEY_END) { move_end(); return; }
    if (key == KEY_PAGEUP) { page_up(); return; }
    if (key == KEY_PAGEDOWN) { page_down(); return; }
    if (key == 'x' || key == KEY_DELETE) { delete_char_forward(); return; }
    if (key == 'd') { pending_delete = 1; set_status("d - waiting for next d"); return; }
    if (key == 'i') { enter_insert_mode(); return; }
    if (key == 'a') { move_right(); enter_insert_mode(); return; }
    if (key == 'o')
    {
        if (insert_line(cursor_line + 1, ""))
        {
            cursor_line = cursor_line + 1;
            cursor_col = 0;
            enter_insert_mode();
        }
        return;
    }
    if (key == 'O')
    {
        if (insert_line(cursor_line, ""))
        {
            cursor_col = 0;
            enter_insert_mode();
        }
        return;
    }
    if (key == ':') { start_command_mode(); return; }
    if (key == KEY_ESCAPE) { set_status(""); return; }
}

void handle_insert_key(int key)
{
    if (key == KEY_ESCAPE)
    {
        exit_insert_mode();
        return;
    }
    if (key == KEY_LEFT)
    {
        move_left();
        return;
    }
    if (key == KEY_RIGHT)
    {
        move_right();
        return;
    }
    if (key == KEY_UP)
    {
        move_up();
        return;
    }
    if (key == KEY_DOWN)
    {
        move_down();
        return;
    }
    if (key == KEY_HOME)
    {
        move_home();
        return;
    }
    if (key == KEY_END)
    {
        move_end();
        return;
    }
    if (key == KEY_PAGEUP)
    {
        page_up();
        return;
    }
    if (key == KEY_PAGEDOWN)
    {
        page_down();
        return;
    }
    if (key == KEY_ENTER)
    {
        insert_newline();
        return;
    }
    if (key == KEY_BACKSPACE)
    {
        backspace_char();
        return;
    }
    if (key == KEY_DELETE)
    {
        delete_char_forward();
        return;
    }
    if (key == KEY_TAB)
    {
        insert_char(' ');
        insert_char(' ');
        return;
    }
    if (key >= 32)
    {
        insert_char(key);
    }
}

void handle_command_key(int key)
{
    if (key == KEY_ESCAPE)
    {
        cancel_command_mode();
        return;
    }
    if (key == KEY_ENTER)
    {
        execute_command();
        return;
    }
    if (key == KEY_BACKSPACE)
    {
        command_backspace();
        return;
    }
    if (key >= 32)
    {
        append_command_char(key);
    }
}

void init_key_constants()
{
    KEY_UP = keycode("up");
    KEY_DOWN = keycode("down");
    KEY_LEFT = keycode("left");
    KEY_RIGHT = keycode("right");
    KEY_DELETE = keycode("delete");
    KEY_ENTER = keycode("enter");
    KEY_ESCAPE = keycode("esc");
    KEY_BACKSPACE = keycode("backspace");
    KEY_HOME = keycode("home");
    KEY_END = keycode("end");
    KEY_PAGEUP = keycode("pageup");
    KEY_PAGEDOWN = keycode("pagedown");
    KEY_TAB = keycode("tab");
    if (KEY_ENTER < 0) KEY_ENTER = 10;
    if (KEY_ESCAPE < 0) KEY_ESCAPE = 27;
    if (KEY_BACKSPACE < 0) KEY_BACKSPACE = 8;
    if (KEY_TAB < 0) KEY_TAB = 9;
}

int main(void)
{
    init_key_constants();
    reset_buffer();
    ensure_min_line();
    char* selected = trim(input("vi file path (default /home/user/vi.txt): "));
    if (strlen(selected) == 0) selected = "/home/user/vi.txt";
    filename = selected;
    open_document(filename);
    ensure_min_line();

    while (running)
    {
        render();
        int key = readkey();
        if (key < 0) continue;
        if (mode == MODE_INSERT)
        {
            handle_insert_key(key);
            continue;
        }
        if (mode == MODE_COMMAND)
        {
            handle_command_key(key);
            continue;
        }
        handle_normal_key(key);
    }

    console_show_cursor(1);
    console_clear();
    printf("bye\n");
    return 0;
}
