# AdamCoulterOz.github.io

Source for [adamcoulteroz.github.io](https://adamcoulteroz.github.io/), a concise index of Adam Coulter's published project sites.

## Local preview

Serve the `site` directory with any static HTTP server, for example:

```sh
python3 -m http.server 8080 --directory site
```

## Deployment

Every push to `main` uploads `site` and deploys it through GitHub Pages.

Add a project only after its public page is live, then update `site/index.html`, `CONTEXT.md`, and `INTERFACE.md` together.
