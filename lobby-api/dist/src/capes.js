// Плащи Aura для CustomSkinLoader: id текстуры → PNG (64x32). Выбор игрока хранится в SkinRecord.cape.
export const CAPES = {
    cape_cyan: 'iVBORw0KGgoAAAANSUhEUgAAAEAAAAAgCAYAAACinX6EAAAA1ElEQVR4nOXDYWcCAQAG4Pf3lGxSZprZJCXNTDNLsp8QEX2IGPsQWSmO4zjKUiqOiNgszZLjOMqIGCP6F+9+yPvw4Gl7pHIU/AOVI+/9UTly7i+V43G1p3I8fO2oHPeLHypH9mND5bib+1SO25lH5biZulSOjLOmcqTH31SO1HBJ5Uj2P6kcid47lSNuz6kc19aMynFlTqkcl4ZD5bhoT6gcseaIynHeGFA5zupvVI7oS5fKEXm2qRzhmkXlOK2aVI6TikHlCJU7VI5gqUXlCBRfqfwfEgr17SzBjQEAAAAASUVORK5CYII=',
    cape_silver: 'iVBORw0KGgoAAAANSUhEUgAAAEAAAAAgCAYAAACinX6EAAAAu0lEQVR4nOXDiwbCAAAF0Pu/ESUiYpqmqZRSxpimlKX0fr80TTVGRL9z+5B7OHgnPypHFH+pHOHzQ+W4PRIqx/keUzkO1xeVY3uKqByrfUjlmG9uVI7p8kLlGM+OVI7RZEflGARrKoc/XFA5vP6MyuH2JlQOxwuoHB13SOVoOQMqR6PjUzlqLY/KYTdcKodVc6gcpt2lchhWm8pRNJtUjoJRp3Lki1UqR65QoXJk8mUqRzpXonKksgaV/wEyuBIasfDO4AAAAABJRU5ErkJggg=='
};
export function isCapeId(v) {
    return v === 'cyan' || v === 'silver';
}
