window.miniOsTerminal = window.miniOsTerminal || {
    scrollToBottom: function (element) {
        if (!element) {
            return;
        }
        element.scrollTop = element.scrollHeight;
    }
};
