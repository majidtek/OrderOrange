// Infinite-scroll sentinel: when the marker div scrolls near the viewport,
// tell the Blazor component to load the next page.
window.mfInfinite = {
    observe(element, dotnetRef) {
        if (!element) return;
        const observer = new IntersectionObserver(entries => {
            if (entries.some(e => e.isIntersecting))
                dotnetRef.invokeMethodAsync('OnSentinelVisible');
        }, { rootMargin: '400px' });
        observer.observe(element);
        element._mfObserver = observer;
    },
    unobserve(element) {
        element?._mfObserver?.disconnect();
    }
};
