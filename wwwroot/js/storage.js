window.storage = {
    get: (key) => {
        const val = localStorage.getItem(key);
        return val ? JSON.parse(val) : null;
    },
    set: (key, value) => {
        localStorage.setItem(key, JSON.stringify(value));
    },
    remove: (key) => {
        localStorage.removeItem(key);
    }
};
