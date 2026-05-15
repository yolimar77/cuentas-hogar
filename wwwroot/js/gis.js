window.gis = {
    _dotNetRef: null,
    _tokenClient: null,

    init: (clientId, dotNetRef) => {
        window.gis._dotNetRef = dotNetRef;
        window.gis._tokenClient = google.accounts.oauth2.initTokenClient({
            client_id: clientId,
            scope: 'https://www.googleapis.com/auth/drive.appdata',
            callback: (response) => {
                if (response.error) {
                    window.gis._dotNetRef.invokeMethodAsync('OnAuthError', response.error);
                } else {
                    window.gis._dotNetRef.invokeMethodAsync('OnAuthSuccess', response.access_token);
                }
            }
        });
    },

    connect: () => {
        if (window.gis._tokenClient) {
            window.gis._tokenClient.requestAccessToken();
        } else {
            console.error('GIS no inicializado');
        }
    },

    disconnect: (token) => {
        google.accounts.oauth2.revoke(token, () => {});
        window.gis._tokenClient = null;
        window.gis._dotNetRef = null;
    }
};
