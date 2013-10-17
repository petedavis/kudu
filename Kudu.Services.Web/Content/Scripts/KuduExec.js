var curWorkingDir = ko.observable("");
window.KuduExec = { workingDir: curWorkingDir };

$(function() {

    // call make console after this first command so the current working directory is set.
    var lastLine = "";
    var lastUserInput = null;
    var kuduExecConsole = $('<div class="console">');
    var curReportFun;
    var controller = kuduExecConsole.console({
        continuedPrompt: true,
        promptLabel: function() {
            return getJSONValue(lastLine);
        },
        commandValidate: function() {
            return true;
        },
        commandHandle: function (line, reportFn) {
            line = line.trim();
            lastUserInput = line + "\n";
            lastLine.Output ? lastLine.Output += lastUserInput : lastLine.Error += lastUserInput;
            curReportFun = reportFn;
            if (!line) {
                reportFn({ msg: "", className: "jquery-console-messae-value" });
            } else if (line === "exit" || line === "cls") {
                controller.reset();
                controller.message("", "jquery-console-message-value");
            } else {
                _sendCommand(line);
                controller.enableInput();
            }
        },
        cancelHandle: function() {
            _sendMessage({ "break": true });
            curReportFun("Command canceled by user.", "jquery-console-message-error");
        },
        completeHandle: function(line) {
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
        welcomeMessage: "Kudu Remote Execution Console\r\nType 'exit' to reset this console.\r\n\r\n"
    });
    $('#KuduExecConsole').append(kuduExecConsole);


    var connection = $.connection('/commandstream');

    connection.start({
        waitForPageLoad: false,
        transport: "auto"
    });

    connection.received(function (data) {
        var prompt = getJSONValue(data);
        if (prompt == lastUserInput)
            return;
        //if the data has the same class as the last ".jquery-console-message"
        //then just append it to the last one, if not, create a new div.
        lastLine = getJSONValue(lastLine);
        var lastConsoleMessage = $(".jquery-console-message").last();
        lastConsoleMessage.text(lastConsoleMessage.text() + lastLine);
        $(".jquery-console-inner").append($(".jquery-console-prompt-box"));
        $(".jquery-console-cursor").parent().prev(".jquery-console-prompt-label").text(prompt);
        controller.promptText("");
        controller.scrollToBottom();
        
        //Now create the div for the new line that will be printed the next time with the correct class
        if (data.Error && !lastConsoleMessage.hasClass("jquery-console-message-error")) {
            controller.message("", "jquery-console-message-error");
        }
        //Also if lastline ends with a new line character this means that it's safe to start a new div
        else if ((data.Output && !lastConsoleMessage.hasClass("jquery-console-message-value")) || endsWith(lastLine, "\n")) {
            controller.message("", "jquery-console-message-value");
        }
        
        //save last line for next time.
        lastLine = data;
    });
    
    function _sendCommand(input) {
        _sendMessage(input);
    }

    function _sendMessage(input) {
        connection.send(input);
    }
    
    function endsWith(str, suffix) {
        return str.indexOf(suffix, str.length - suffix.length) !== -1;
    }
    
    function getJSONValue(input) {
        return (input.Output || input.Error || "").toString()
    }

});
    