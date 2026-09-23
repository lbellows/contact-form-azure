(() => {
  const form = document.getElementById("contactForm");
  const statusEl = document.getElementById("status");
  const submitBtn = document.getElementById("submitBtn");
  const params = new URLSearchParams(window.location.search);
  // Embeds use /form/<site>, whose route sets the CSP naming the domains allowed to frame it.
  const pathSite = (window.location.pathname.match(/^\/form\/([A-Za-z0-9_-]+)\/?$/) || [])[1];
  const site = pathSite || params.get("site") || "";

  const emailRe = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;

  // Turnstile renders only when the API publishes a site key, so the form works
  // unchanged until the widget is configured.
  const turnstileEl = document.getElementById("turnstile");
  let turnstileWidget = null;
  fetch("/api/config")
    .then((r) => (r.ok ? r.json() : {}))
    .then(({ turnstileSiteKey }) => {
      if (!turnstileSiteKey) return;
      window.onTurnstileLoad = () => {
        turnstileEl.classList.remove("hidden");
        turnstileWidget = window.turnstile.render(turnstileEl, { sitekey: turnstileSiteKey });
      };
      const script = document.createElement("script");
      script.src = "https://challenges.cloudflare.com/turnstile/v0/api.js?render=explicit&onload=onTurnstileLoad";
      script.async = true;
      document.head.appendChild(script);
    })
    .catch(() => {});

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
      turnstileToken: turnstileWidget !== null ? window.turnstile.getResponse(turnstileWidget) || "" : undefined,
    };

    const errors = validate(payload);
    if (turnstileWidget !== null && !payload.turnstileToken) {
      errors.push("Please complete the verification.");
    }
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
      } else {
        const message = result.error === "captcha_failed"
          ? "Verification failed. Please try again."
          : result.error === "forbidden_site"
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
      // Tokens are single-use; get a fresh one for any retry.
      if (turnstileWidget !== null) window.turnstile.reset(turnstileWidget);
      submitBtn.disabled = false;
    }
  });
})();
