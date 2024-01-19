package com.t34400.webviewtexture;

public class WebViewJavaScriptConstants
{
    public static final String ANDROID_INTERFACE_INSTANCE_NAME = "Android";

    public static final String SCRIPT__ADD_INPUT_FOCUS_LISTENER =
        "let currentFocusedInput;" +
        "(function() {" +
        "    var inputs = document.querySelectorAll('input');" +
        "    inputs.forEach(function(input) {" +
        "       if(input._eventHandlersAdded) {" +
        "          return;" +
        "       }" +
        "       input.addEventListener('focus', function() {" +
        "           currentFocusedInput = input;" +
        "           console.log('onInputFocusAcquired()');" +
        "           " + ANDROID_INTERFACE_INSTANCE_NAME + ".onInputFocusAcquired(input.type, input.value);" +
        "       });" +
        "       input.addEventListener('blur', function() {" +
        "           currentFocusedInput = null;" +
        "           console.log('onInputFocusLost()');" +
        "           " + ANDROID_INTERFACE_INSTANCE_NAME + ".onInputFocusLost();" +
        "       });" +
        "       input._eventHandlersAdded = true;" +
        "    });" +
        "" +
        "    function processNode(node, array) {" +
        "        if(node.tagName === 'INPUT') {" +
        "            array.push(node);" +
        "        }" +
        "        if(node.childNodes) {" +
        "            node.childNodes.forEach((childNode) => {" +
        "                    processNode(childNode, array);" +
        "                });" +
        "        }" +
        "    }" +
        "" +
        "    const parentElement = document.getElementsByTagName('BODY')[0];" +
        "    const mutationCallback = (mutationsList, observer) => {" +
        "        for (const mutation of mutationsList) {" +
        "            if (mutation.type === 'childList') {" +
        "                const addedNodes = Array.from(mutation.addedNodes);" +
        "                const addedInputs = [];" +
        "                addedNodes.forEach((addedNode) => processNode(addedNode, addedInputs));" +
        "                " +
        "                for (const addedInput of addedInputs) {" +
        "                    addedInput.addEventListener('focus', function() {" +
        "                        currentFocusedInput = input;" +
        "                        console.log('onInputFocusAcquired()');" +
        "                        " + ANDROID_INTERFACE_INSTANCE_NAME + ".onInputFocusAcquired(addedInput.type, addedInput.value);" +
        "                    });" +
        "                    addedInput.addEventListener('blur', function() {" +
        "                        currentFocusedInput = null;" +
        "                        console.log('onInputFocusLost()');" +
        "                        " + ANDROID_INTERFACE_INSTANCE_NAME + ".onInputFocusLost();" +
        "                    });" +
        "                    addedInput._eventHandlersAdded = true;" +
        "                }" +
        "                " +
        "                const removedInputs = Array.from(mutation.removedNodes).filter(node => node.tagName === 'INPUT');" +
        "                for (const removedInput of removedInputs) {" +
        "                    if(removedInput === currentFocusedInput) {" +
        "                        currentFocusedInput = null;" +
        "                        console.log('onInputFocusLost()');" +
        "                        " + ANDROID_INTERFACE_INSTANCE_NAME + ".onInputFocusLost();" +
        "                    }" +
        "                }" +
        "            }" +
        "        }" +
        "    };" +
        "" +
        "    const observer = new MutationObserver(mutationCallback);" +
        "    const config = { childList: true, subtree: true };" +
        "    observer.observe(parentElement, config);" +
        "} ());";

    public static final String SCRIPT__SET_INPUT_VALUE =
            "var newValue = '%s';" +
                    "var focusedElement = document.activeElement;" +
                    "if (focusedElement && focusedElement.tagName === 'INPUT') {" +
                    "   var nativeInputValueSetter = Object.getOwnPropertyDescriptor(window.HTMLInputElement.prototype, 'value').set;" +
                    "   nativeInputValueSetter.call(focusedElement, newValue);" +
                    "   focusedElement.value = newValue;" +
                    "   focusedElement.dispatchEvent(new Event('input', { bubbles: true }));" +
                    "} else if (focusedElement && focusedElement.tagName === 'TEXTAREA') {" +
                    "   var nativeTextAreaValueSetter = Object.getOwnPropertyDescriptor(window.HTMLTextAreaElement.prototype, 'value').set;" +
                    "   nativeTextAreaValueSetter.call(focusedElement, newValue);" +
                    "   focusedElement.value = newValue;" +
                    "   focusedElement.dispatchEvent(new Event('input', { bubbles: true }));" +
                    "}";

    public static final String SCRIPT__REMOVE_FOCUS =
            "var focusedElement = document.activeElement;" +
                    "if (focusedElement && (focusedElement.tagName === 'INPUT' || focusedElement.tagName === 'TEXTAREA')) {" +
                    "   document.body.dispatchEvent(new KeyboardEvent('keydown', { bubbles:true, cancelable: true, key: 'Enter' }));" + 
                    "   focusedElement.blur();" +
                    "}";

    public static final String SCRIPT__INSERT_INPUT_BY_ID =
                    "var input = document.getElementById('%s');" +
                            "if(input && (input.tagName === 'INPUT' || input.tagName === 'TEXTAREA')) {" +
                            "    input.value = '%s';" +
                            "    input.dispatchEvent(new Event('input', { bubbles: true }));" +
                            "}";
        
    public static final String SCRIPT__INSERT_PASSWORD_INPUT =
                    "var input = document.querySelector('input[type=\"password\"]');" +
                            "if(input) {" +
                            "    input.value = '%s';" +
                            "    input.dispatchEvent(new Event('input', { bubbles: true }));" +
                            "}";

    public static final String SCRIPT__CLICK_BUTTON_BY_ID =
            "var button = document.getElementById('login-button');" +
                    "if(button && button.tagName == 'BUTTON') {" +
                    "    button.click();" +
                    "}";

    public static final String SCRIPT__TRY_CLICK_SUBMIT_BUTTON =
            "var submitElement = document.querySelector('input[type=\"submit\"], button[type=\"submit\"]');" +
                    "if(submitElement) {" +
                    "    submitElement.click();" +
                    "}";
}