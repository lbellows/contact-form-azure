(() => {
  const form = document.getElementById("contactForm");
  const statusEl = document.getElementById("status");
  const submitBtn = document.getElementById("submitBtn");
  const siteInput = document.getElementById("site");

  const params = new URLSearchParams(window.location.search);
  const site = params.get("site") || "";
  siteInput.value = site;

  const emailRe = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;

  const setStatus = (message, isOk) => {
    statusEl.textContent = message;
    statusEl.classList.toggle("ok", Boolean(isOk));
    statusEl.classList.toggle("err", !isOk);
  };

  const validate = (data) => {
    const errors = [];
    if (!data.name.trim()) errors.push("Name is required.");
    if (!data.email.trim()) errors.push("Email is required.");
    if (data.email && !emailRe.test(data.email)) errors.push("Email looks invalid.");
    if (!data.message.trim()) errors.push("Message is required.");
    return errors;
  };

  form.addEventListener("submit", async (event) => {
    event.preventDefault();
    setStatus("", true);

    const payload = {
      name: form.name.value.trim(),
      email: form.email.value.trim(),
      subject: form.subject.value.trim(),
      message: form.message.value.trim(),
      site,
      company: form.company.value.trim(),
    };

    const errors = validate(payload);
    if (errors.length) {
      setStatus(errors[0], false);
      return;
    }

    submitBtn.disabled = true;
    setStatus("Sending…", true);

    try {
      const response = await fetch("/api/submit", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(payload),
      });
      const result = await response.json().catch(() => ({}));
      if (response.ok && result.ok) {
        setStatus("Thanks! Your message has been sent.", true);
        form.reset();
        siteInput.value = site;
      } else {
        const message = result.error === "forbidden_site"
          ? "This form is not configured for this site."
          : result.error === "rate_limited"
            ? "Please wait a bit before sending another message."
            : result.error === "validation_error"
              ? (result.details && result.details[0]) || "Check your details and try again."
              : "Something went wrong. Please try again.";
        setStatus(message, false);
      }
    } catch (err) {
      setStatus("Network error. Please try again.", false);
    } finally {
      submitBtn.disabled = false;
    }
  });
})();
