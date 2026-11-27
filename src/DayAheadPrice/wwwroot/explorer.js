// Updates the on-page Time/Price headers.
function setPriceData(priceString, timeString) {
    const timeHeader = document.getElementById("TimeHeader");
    const priceHeader = document.getElementById("PriceHeader");

    if (timeHeader) {
        timeHeader.textContent = timeString;
    }

    if (priceHeader) {
        priceHeader.textContent = priceString;
    }
}

// One delegated listener for every histogram bar, which carries its texts in data-price/data-time attributes.
// Registered on the document so it keeps working however Blazor re-renders the chart. The axis column shares
// the histogrambar class but carries no data, so only bars with data-price are matched.
function showBarData(event) {
    const bar = event.target instanceof Element ? event.target.closest(".histogrambar[data-price]") : null;

    if (bar) {
        setPriceData(bar.dataset.price, bar.dataset.time);
    }
}

document.addEventListener("mouseover", showBarData);
document.addEventListener("touchstart", showBarData, { passive: true });

// Document-level keyboard navigation for the price explorer. A global listener is used so the
// arrow/End keys work without the component first having to be focused. Modified keys (e.g. Alt+Left for
// browser Back) and keys typed into editable elements are left to the browser.
window.dapExplorer = {
    _handler: null,
    register: function (dotNetRef) {
        this.unregister();

        this._handler = function (event) {
            if (event.altKey || event.ctrlKey || event.metaKey || event.shiftKey || isEditable(event.target)) {
                return;
            }

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

function isEditable(element) {
    return element instanceof HTMLElement
        && (element.isContentEditable || element.closest("input, select, textarea") !== null);
}
