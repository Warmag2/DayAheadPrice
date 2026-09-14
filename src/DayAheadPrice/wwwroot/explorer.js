// Updates the on-page Time/Price headers from the inline hover handlers on histogram bars.
function setPriceData(_, priceString, timeString) {
    const timeHeader = document.getElementById("TimeHeader");
    const priceHeader = document.getElementById("PriceHeader");

    if (timeHeader) {
        timeHeader.innerHTML = timeString;
    }

    if (priceHeader) {
        priceHeader.innerHTML = priceString;
    }
}

// Document-level keyboard navigation for the price explorer. A global listener is used so the
// arrow/End keys work without the component first having to be focused.
window.dapExplorer = {
    _handler: null,
    register: function (dotNetRef) {
        this.unregister();

        this._handler = function (event) {
            switch (event.key) {
                case "ArrowLeft":
                case "ArrowRight":
                case "ArrowUp":
                case "ArrowDown":
                case "End":
                    event.preventDefault();
                    dotNetRef.invokeMethodAsync("HandleKey", event.key);
                    break;
                default:
                    break;
            }
        };

        document.addEventListener("keydown", this._handler);
    },
    unregister: function () {
        if (this._handler) {
            document.removeEventListener("keydown", this._handler);
            this._handler = null;
        }
    }
};
