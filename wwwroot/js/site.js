// Please see documentation at https://learn.microsoft.com/aspnet/core/client-side/bundling-and-minification
// for details on configuring this project to bundle and minify static web assets.

document.querySelectorAll("form[data-add-to-watchlist]").forEach(form => {
    form.addEventListener("submit", async event => {
        event.preventDefault();

        const button = form.querySelector("button[type='submit']");
        const feedback = form.querySelector(".watchlist-add-feedback");
        if (button.disabled) {
            return;
        }

        button.disabled = true;
        feedback.textContent = "";

        try {
            const response = await fetch(form.action, {
                method: "POST",
                body: new FormData(form),
                headers: { Accept: "application/json" },
                credentials: "same-origin",
                redirect: "error"
            });
            if (!response.ok || (await response.json()).added !== true) {
                throw new Error("Watchlist save failed.");
            }

            button.textContent = "✓ Added to Watchlist";
            button.classList.replace("btn-outline-primary", "btn-success");
            feedback.classList.add("visually-hidden");
            feedback.textContent = "Added to your watchlist.";
        } catch {
            button.disabled = false;
            feedback.classList.remove("visually-hidden");
            feedback.textContent = "Couldn’t save. Please try again.";
        }
    });
});
