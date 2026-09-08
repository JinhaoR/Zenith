import './environment.js';
import 'fast-text-encoding';
import 'url-search-params-polyfill';
import { Config, FiltersEngine, Request, parseFilters } from '@ghostery/adblocker';

let engine;
export function load(text) {
  const config = new Config({
    loadCSPFilters: false,
    loadExtendedSelectors: false,
    enableHtmlFiltering: false,
    enableMutationObserver: false,
  });
  const parsed = parseFilters(text, config);
  // Custom style actions are distinct from extended selectors in this engine.
  // Keep only declarative hiding selectors and their corresponding exceptions.
  parsed.cosmeticFilters = parsed.cosmeticFilters.filter(filter =>
    !filter.hasCustomStyle() && !filter.isScriptInject() && !filter.isExtended()
    && !/[{}]/.test(filter.getSelector()));
  engine = new FiltersEngine({ ...parsed, config });
}

export function blocks(json) {
  return engine.match(Request.fromRawDetails(JSON.parse(json))).match;
}

export function counts() {
  const { networkFilters, cosmeticFilters } = engine.getFilters();
  return JSON.stringify({ network: networkFilters.length, cosmetic: cosmeticFilters.length });
}

export function cosmetics(json) {
  const details = JSON.parse(json);
  const request = Request.fromRawDetails({ url: details.url, type: 'main_frame' });
  return engine.getCosmeticsFilters({
    ...details,
    hostname: request.hostname,
    domain: request.domain,
    getInjectionRules: false,
    getExtendedRules: false,
  }).styles;
}
