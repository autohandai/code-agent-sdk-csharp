#include <stdio.h>
#include <stdlib.h>
#include <string.h>

int main(void) {
    char *line = NULL;
    size_t capacity = 0;
    while (getline(&line, &capacity, stdin) != -1) {
        char *id_marker = strstr(line, "\"id\":");
        long id = id_marker == NULL ? 0 : strtol(id_marker + 5, NULL, 10);
        printf("{\"jsonrpc\":\"2.0\",\"id\":%ld,\"result\":{\"status\":\"idle\","
               "\"sessionId\":\"benchmark\",\"model\":\"fixture\"}}\n", id);
        fflush(stdout);
    }
    free(line);
    return 0;
}
