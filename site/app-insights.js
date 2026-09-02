/*
 * This asset deliberately uses the official Application Insights browser SDK
 * loaded by index.html. Keep it CSP-friendly: no inline script or telemetry
 * configuration is committed here. A production CSP must allow the SDK host
 * and the ingestion endpoint from the generated connection string.
 */
(function (window, document) {
    "use strict";

    var rootPageUri = "https://adamcoulteroz.github.io/";
    var connectionString = window.__appInsightsConnectionString;
    var applicationInsights = window.Microsoft && window.Microsoft.ApplicationInsights;

    if (typeof connectionString !== "string" || connectionString.length === 0 || !applicationInsights) {
        return;
    }

    var telemetry = new applicationInsights.ApplicationInsights({
        config: {
            connectionString: connectionString,
            disableCookiesUsage: true,
            cookieCfg: { enabled: false },
            isStorageUseDisabled: true,
            enableSessionStorageBuffer: false,
            disableAjaxTracking: true,
            disableFetchTracking: true,
            disableCorrelationHeaders: true,
            enableAutoRouteTracking: false,
            autoTrackPageVisitTime: false,
            disableExceptionTracking: true,
            enableUnhandledPromiseRejectionTracking: false
        }
    });

    telemetry.addTelemetryInitializer(function (item) {
        var bases = [item && item.baseData, item && item.data && item.data.baseData];

        bases.forEach(function (base) {
            if (!base || typeof base !== "object") {
                return;
            }

            ["uri", "refUri", "url", "referrer"].forEach(function (field) {
                if (Object.prototype.hasOwnProperty.call(base, field)) {
                    base[field] = "";
                }
            });
        });

        if (item && item.baseType === "PageviewData" && item.baseData) {
            item.baseData.uri = rootPageUri;
            item.baseData.refUri = "";
        }

        return true;
    });

    telemetry.loadAppInsights();
    telemetry.trackPageView({
        name: "home",
        uri: rootPageUri,
        refUri: ""
    });

    var destinations = {
        project: {
            bandwidth_calculator: true,
            fluent_icon_browser: true,
            meridian: true,
            retro_texture_studio: true
        },
        contact: {
            email: true,
            linkedin: true,
            github: true
        }
    };

    function eventId() {
        if (!window.crypto || typeof window.crypto.randomUUID !== "function") {
            return null;
        }

        return window.crypto.randomUUID();
    }

    function trackLinkClick(event) {
        var link = event.currentTarget;
        var kind = link.dataset.telemetryKind;
        var destination = link.dataset.telemetryDestination;

        if (!destinations[kind] || destinations[kind][destination] !== true) {
            return;
        }

        var archiveEventId = eventId();
        if (!archiveEventId) {
            return;
        }

        telemetry.trackEvent({
            name: kind === "project" ? "project_click" : "contact_click"
        }, {
            destination: destination,
            archive_event_id: archiveEventId
        });
    }

    document.querySelectorAll("[data-telemetry-kind][data-telemetry-destination]").forEach(function (link) {
        link.addEventListener("click", trackLinkClick);
    });
}(window, document));
