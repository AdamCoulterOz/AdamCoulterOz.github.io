const assert = require("node:assert/strict");
const fs = require("node:fs");
const vm = require("node:vm");

const calls = [];
let createdInstance;
const links = [{
    dataset: { telemetryKind: "project", telemetryDestination: "meridian" },
    addEventListener(_name, handler) {
        this.handler = handler;
    }
}];

function ApplicationInsights(options) {
    createdInstance = this;
    this.options = options;
    this.addTelemetryInitializer = (initializer) => {
        this.initializer = initializer;
    };
    this.loadAppInsights = () => calls.push(["load"]);
    this.trackPageView = (pageView) => calls.push(["page", pageView]);
    this.trackEvent = (event, properties) => calls.push(["event", event, properties]);
}

const context = {
    window: {
        __appInsightsConnectionString: "public-test-value",
        crypto: { randomUUID: () => "00000000-0000-4000-8000-000000000000" },
        Microsoft: { ApplicationInsights: { ApplicationInsights } }
    },
    document: { querySelectorAll: () => links }
};

vm.runInNewContext(fs.readFileSync("site/app-insights.js", "utf8"), context);

assert.equal(createdInstance.options.config.disableCookiesUsage, true);
assert.equal(createdInstance.options.config.isStorageUseDisabled, true);
assert.equal(calls[0][0], "load");
assert.deepEqual(JSON.parse(JSON.stringify(calls[1])), ["page", {
    name: "home",
    uri: "https://adamcoulteroz.github.io/",
    refUri: ""
}]);
links[0].handler({ currentTarget: links[0] });
assert.deepEqual(JSON.parse(JSON.stringify(calls[2])), ["event", { name: "project_click" }, {
    destination: "meridian",
    archive_event_id: "00000000-0000-4000-8000-000000000000"
}]);

const pageEnvelope = {
    baseType: "PageviewData",
    baseData: {
        uri: "https://example.invalid/private?token=secret",
        refUri: "https://referrer.invalid/private?token=secret",
        url: "https://example.invalid/private?token=secret",
        referrer: "https://referrer.invalid/private?token=secret"
    }
};
assert.equal(createdInstance.initializer(pageEnvelope), true);
assert.deepEqual(JSON.parse(JSON.stringify(pageEnvelope.baseData)), {
    uri: "https://adamcoulteroz.github.io/",
    refUri: "",
    url: "",
    referrer: ""
});
