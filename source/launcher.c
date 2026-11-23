typedef unsigned short Wide;
typedef unsigned long long Word;
typedef unsigned int Dword;
typedef void *Handle;
#define API __attribute__((ms_abi))
#define W(s) ((const Wide *)L##s)
typedef struct {
    Dword size;
    Wide *reserved, *desktop, *title;
    Dword x, y, width, height, xCount, yCount, fill, flags;
    unsigned short show, reservedCount;
    unsigned char *reservedData;
    Handle input, output, error;
} Startup;
typedef struct { Handle process, thread; Dword pid, tid; } Process;
struct {
    Dword (API *moduleName)(Handle, Wide *, Dword);
    Dword (API *tempPath)(Dword, Wide *);
    Dword (API *tempName)(const Wide *, const Wide *, Dword, Wide *);
    Handle (API *createFile)(const Wide *, Dword, Dword, void *, Dword, Dword, Handle);
    int (API *writeFile)(Handle, const void *, Dword, Dword *, void *);
    int (API *close)(Handle);
    int (API *deleteFile)(const Wide *);
    Dword (API *systemDir)(Wide *, Dword);
    int (API *setEnv)(const Wide *, const Wide *);
    Wide *(API *getCommandLine)(void);
    int (API *start)(const Wide *, Wide *, void *, void *, int, Dword, void *, const Wide *, Startup *, Process *);
    Dword (API *wait)(Handle, Dword);
    int (API *exitCode)(Handle, Dword *);
    void (API *exit)(Dword);
    void *kernelEnd;
    int (API *message)(Handle, const Wide *, const Wide *, Dword);
    void *userEnd;
} imports __attribute__((section(".iat"))) = {0};
__attribute__((section(".impdata"), used)) static const unsigned char importSpace[2048] = {1};
#include "editor_bytes.h"
static Wide folder[32768], scratch[32768], path[32768], executable[32768], shell[32768], command[32768];
static Startup startup;
static Process process;
static void append(Wide *dest, const Wide *src) {
    while (*dest) dest++;
    while ((*dest++ = *src++)) {}
}
static int installer(const Wide *line) {
    if (*line == '"') { line++; while (*line && *line != '"') line++; if (*line) line++; }
    else while (*line && *line != ' ') line++;
    while (*line == ' ') line++;
    const Wide *option = W("--install");
    while (*option && *line == *option) { line++; option++; }
    return !*option && (!*line || *line == ' ');
}
void API entry(void) {
    Dword result = 1, written = 0;
    if (!imports.moduleName(0, executable, 32768) || !imports.systemDir(shell, 32768) ||
        !imports.tempPath(32768, folder) || !imports.tempName(folder, W("fcs"), 0, scratch)) goto cleanup;
    append(path, scratch);
    append(path, W(".ps1"));
    Handle file = imports.createFile(path, 0x40000000, 0, 0, 1, 0x80, 0);
    if (file == (Handle)-1) goto cleanup;
    int success = imports.writeFile(file, editor, sizeof(editor), &written, 0);
    imports.close(file);
    if (!success || written != sizeof(editor)) goto cleanup;
    append(shell, W("\\WindowsPowerShell\\v1.0\\powershell.exe"));
    append(command, W("\""));
    append(command, shell);
    append(command, W("\" -NoProfile -NonInteractive -STA -ExecutionPolicy Bypass -File \""));
    append(command, path);
    append(command, W("\" -Mode "));
    append(command, installer(imports.getCommandLine()) ? W("Install") : W("Editor"));
    startup.size = sizeof(startup);
    if (imports.setEnv(W("TRICKS_STUDIO_EXE"), executable) &&
        imports.start(shell, command, 0, 0, 0, 0x08000000, 0, 0, &startup, &process)) {
        imports.close(process.thread);
        imports.wait(process.process, 0xffffffff);
        imports.exitCode(process.process, &result);
        imports.close(process.process);
    }
cleanup:
    if (path[0]) imports.deleteFile(path);
    if (scratch[0]) imports.deleteFile(scratch);
    if (result) imports.message(0, W("Tricks Studio could not start. Windows PowerShell and WPF are required."), W("Tricks Studio"), 16);
    imports.exit(result);
}
