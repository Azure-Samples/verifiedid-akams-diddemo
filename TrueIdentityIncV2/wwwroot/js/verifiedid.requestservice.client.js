function RequestService(onDrawQRCode, onNavigateToDeepLink, onRequestRetrieved, onRequestCancelled, onPresentationVerified, onIssuanceSuccesful, onSelfieTaken, onError, pollFrequency) {
    this.onDrawQRCode = onDrawQRCode;
    this.onNavigateToDeepLink = onNavigateToDeepLink;
    this.onRequestRetrieved = onRequestRetrieved;
    this.onRequestCancelled = onRequestCancelled;
    this.onPresentationVerified = onPresentationVerified;
    this.onIssuanceSuccesful = onIssuanceSuccesful;
    this.onSelfieTaken = onSelfieTaken;
    this.onError = onError;
    this.pollFrequency = (pollFrequency == undefined ? 1000 : pollFrequency);
    this.apiCreateIssuanceRequest = '/api/issuer/issuance-request';
    this.apiSetPhoto = '/api/issuer/userphoto';
    this.apiCreateSelfieRequest = '/api/issuer/selfie-request';
    this.apiCreatePresentationRequest = '/api/verifier/presentation-request';
    this.apiPollPresentationRequest = '/api/request-status';
    this.logEnabled = false;
    this.log = function (msg) {
        if (this.logEnabled) {
            console.log(msg);
        }
    }
    this.request = null;
    this.requestType = "";
    this.cancelPolling = false;
    this.uuid = 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'
        .replace(/[xy]/g, function (c) {
            const r = Math.random() * 16 | 0, v = c == 'x' ? r : (r & 0x3 | 0x8);
            return v.toString(16);
        });

    // function to create a presentation request
    this.createRequest = async function (url) {
        this.cancelPolling = false;
        const response = await fetch(url, { method: 'GET', headers: { 'Accept': 'application/json', 'rsid': this.uuid } });
        const respJson = await response.json();
        console.log(respJson);
        if (respJson.error_description) {
            this.onError(this.requestType, "error", null, respJson.error_description);
        } else {
            this.request = respJson;
            if (/Android/i.test(navigator.userAgent) || /iPhone/i.test(navigator.userAgent)) {
                this.log(`Mobile device (${navigator.userAgent})! Using deep link (${respJson.url}).`);
                this.onNavigateToDeepLink(this.requestType, "deeplink", respJson.id, respJson.url);
            } else {
                this.log(`Not Android or IOS. Generating QR code`);
                this.onDrawQRCode(this.requestType, respJson.id, respJson.url, respJson.pin);
                this.pollRequestStatus(respJson.id);
            }
        }
    };
    this.createPresentationRequest = function (callback) {
        this.requestType = "presentation";
        if (callback) {
            this.onPresentationVerified = callback;
            this.onRequestRetrieved = callback;
            this.onRequestCancelled = callback;
            this.onError = callback;
        }
        this.createRequest(this.apiCreatePresentationRequest)
    };
    this.createIssuanceRequest = function (callback) {
        this.requestType = "issuance";
        if (callback) {
            this.onIssuanceSuccesful = callback;
            this.onRequestRetrieved = callback;
            this.onRequestCancelled = callback;
            this.onError = callback;
        }
        this.createRequest(this.apiCreateIssuanceRequest)
    };
    this.createSelfieRequest = function (callback) {
        this.requestType = "selfie";
        if (callback) {
            this.onSelfieTaken = callback;
            this.onRequestRetrieved = callback;
            this.onRequestCancelled = callback;
            this.onError = callback;
        }
        this.createRequest(this.apiCreateSelfieRequest);
    };

    this.cancelRequest = function () {
        this.cancelPolling = true;
    };
    // function to pull for presentation status
    this.pollRequestStatus = function (id) {
        var _rsThis = this;
        var pollFlag = setInterval(async function () {
            if (_rsThis.cancelPolling == true) {
                _rsThis.cancelPolling = false;
                clearInterval(pollFlag);
                _rsThis.log(`polling cancelled`);
                _rsThis.onRequestCancelled(_rsThis.requestType, "cancelled", id
                    , { error: "cancelled", error_description: `${_rsThis.requestType} request was cancelled` });
                return;
            }
            var tmNow = (new Date()).getTime() / 1000;
            if ((tmNow - 10) > _rsThis.request.expiry) {
                clearInterval(pollFlag);
                _rsThis.log(`${(tmNow - 10)} > ${_rsThis.request.expiry}`);
                _rsThis.onError(_rsThis.requestType, "timeout", id
                    , { error: "timeout", error_description: `The ${_rsThis.requestType} request was not process in time.` });
                return;
            }
            const response = await fetch(`${_rsThis.apiPollPresentationRequest}?id=${id}`);
            const respMsg = await response.json();
            _rsThis.log(respMsg);
            if (respMsg.error_description) {
                clearInterval(pollFlag);
                _rsThis.onError(this.requestType, "error", id, respMsg);
            } else {
                switch (respMsg.status) {
                    case 'request_retrieved':
                        _rsThis.log(`onRequestRetrieved()`);
                        _rsThis.onRequestRetrieved(_rsThis.requestType, "retrieved", id);
                        break;
                    case 'presentation_verified':
                        clearInterval(pollFlag);
                        _rsThis.log(`onPresentationVerified( ${id}, ... )`);
                        _rsThis.onPresentationVerified(_rsThis.requestType, "verified", id, respMsg);
                        break;
                    case 'issuance_successful':
                        clearInterval(pollFlag);
                        _rsThis.log(`onIssuanceSuccesful( ${id}, ... )`);
                        _rsThis.onIssuanceSuccesful(_rsThis.requestType, "issued", id, respMsg);
                        break;
                    case 'selfie_taken':
                        clearInterval(pollFlag);
                        _rsThis.log(`onSelfieTaken( ${id}, ... )`);
                        _rsThis.onSelfieTaken(_rsThis.requestType, "selfie_taken", id, respMsg);
                        break;
                    case 'presentation_error': case 'issuance_error':
                        clearInterval(pollFlag);
                        _rsThis.log(`onError(...)`);
                        _rsThis.onError(this.requestType, id, "error", respMsg);
                        break;
                }
            }
        }, this.pollFrequency);
    }; // pollRequestStatus

    this.setUserPhoto = async function (base64Image) {
        this.log('setUserPhoto(): ' + base64Image);
        const response = await fetch(this.apiSetPhoto, {
            headers: { 'Accept': 'application/json', 'Content-Type': 'image/jpeg', 'rsid': this.uuid },
            method: 'POST',
            body: base64Image
        });
        const respJson = await response.json();
        this.log(respJson);
        if (respJson.error_description) {
            this.onError(this.requestType, respJson.error_description);
            return -1;
        } else {
            return respJson.id;
        }
    };

} // RequestService
