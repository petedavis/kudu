var curWorkingDir = ko.observable("");
window.KuduExec = { workingDir: curWorkingDir };

$(function () {
    function _changeDir(value) {
        value = value || window.KuduExec.appRoot;
        curWorkingDir(value);
        $(".jquery-console-cursor").parent().prev(".jquery-console-prompt-label").text(value.replace(/\/|\\$/, '') + ">");
    };

    window.KuduExec.changeDir = _changeDir;

    // call make console after this first command so the current working directory is set.
    var connection = $.connection('/commandstream'),
        kuduExecConsole = $('<div class="console">'),
        curReportFun = function () { },
        controller;

    connection.start().done(function () {
        var initial = true;
        var result = curWorkingDir.subscribe(function (newValue) {
            // Unsubscribe from listening to the curWorkingDir.
            result.dispose();
            window.KuduExec.appRoot = curWorkingDir();
            controller = kuduExecConsole.console({
                continuedPrompt: true,
                promptLabel: function () {
                    return curWorkingDir() + ">";
                },
                commandValidate: function () {
                    return true;
                },
                commandHandle: function (line, reportFn) {
                    line = line.trim();
                    curReportFun = reportFn;

                    if (!line) {
                        reportFn({ msg: "", className: "jquery-console-messae-value" });
                    } else if (line === "exit" || line === "cls") {
                        controller.reset();
                    } else {
                        _sendCommand(line);
                        controller.enableInput();
                    }
                },
                cancelHandle: function () {
                    _sendMessage({ "break": true });
                    curReportFun("Command canceled by user.", "jquery-console-message-error");
                },
                completeHandle: function (line) {
                    var cdRegex = /^cd\s+(.+)$/,
                        pathRegex = /.+\s+(.+)/,
                        matches;
                    if (matches = line.match(cdRegex)) {
                        return window.KuduExec.completePath(matches[1], /* dirOnly */ true);
                    } else if (matches = line.match(pathRegex)) {
                        return window.KuduExec.completePath(matches[1]);
                    }
                    return;
                },
                cols: 3,
                autofocus: true,
                animateScroll: true,
                promptHistory: true,
                welcomeMessage: "Kudu Remote Execution Console\nType 'exit' to reset this console."
            });
            $('#KuduExecConsole').append(kuduExecConsole);
        });
        _sendCommand("echo.");

        connection.received(function (data) {
            if (data.CurrentDirectory) {
                curWorkingDir(data.CurrentDirectory);
                curReportFun();
                // _changeDir(data.CurrentDirectory);
            } else if (controller) {
                // need to do some massaging of newlines to make it look right
                var text = (data.Output || data.Error || "").replace(/\r\n/g, '\n'),
                    className = data.Error ? "jquery-console-message-error" : "jquery-console-message-value";
                controller.message(text, className, /*ignorePromptBox*/true);
            }
        });
    });

    function _sendCommand(input) {
        _sendMessage({ command: input, dir: curWorkingDir() });
    }

    function _sendMessage(input) {
        connection.send(JSON.stringify(input));
    }
});


